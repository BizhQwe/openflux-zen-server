package l3guard

import (
	"bytes"
	"errors"
	"fmt"
	"time"

	"github.com/google/nftables"
	"github.com/google/nftables/binaryutil"
	"github.com/google/nftables/expr"
	"github.com/google/nftables/userdata"
	"golang.org/x/sys/unix"
)

type systemBackend struct{}

const flowSet = "tcp_flows"
const flowTTL = 6 * time.Minute

func (systemBackend) owned(g *Guard) (bool, error) {
	c := &nftables.Conn{}
	tables, e := c.ListTablesOfFamily(nftables.TableFamilyIPv4)
	if e != nil {
		return false, e
	}
	for _, table := range tables {
		if table.Name != g.table {
			continue
		}
		sets, e := c.GetSets(table)
		if e != nil {
			return false, e
		}
		if len(sets) == 1 && sets[0].Name == flowSet && sets[0].KeyType.Bytes == 16 {
			rules, e := c.GetRules(table, &nftables.Chain{Name: "output", Table: table})
			if e != nil {
				return false, e
			}
			owned := len(rules) == 3
			for _, rule := range rules {
				owned = owned && bytes.Equal(rule.UserData, userdata.AppendString(nil, userdata.TypeComment, g.owner))
			}
			if owned {
				return true, nil
			}
		}
		return false, fmt.Errorf("отказ изменять чужую таблицу nftables: %s", g.table)
	}
	return false, nil
}
func (systemBackend) remove(g *Guard) error {
	c := &nftables.Conn{}
	c.DelTable(&nftables.Table{Name: g.table, Family: nftables.TableFamilyIPv4})
	return c.Flush()
}
func matchMark(mark uint32) []expr.Any {
	return []expr.Any{&expr.Meta{Key: expr.MetaKeyMARK, Register: 1}, &expr.Cmp{Op: expr.CmpOpEq, Register: 1, Data: binaryutil.NativeEndian.PutUint32(mark)}}
}
func matchTCP() []expr.Any {
	return []expr.Any{&expr.Meta{Key: expr.MetaKeyL4PROTO, Register: 1}, &expr.Cmp{Op: expr.CmpOpEq, Register: 1, Data: []byte{unix.IPPROTO_TCP}}}
}
func tuple() []expr.Any {
	// A concatenated key uses four padded 32-bit registers. Zero the two
	// port registers first; only their first two bytes are packet payload.
	return []expr.Any{
		&expr.Payload{DestRegister: unix.NFT_REG32_00, Base: expr.PayloadBaseNetworkHeader, Offset: 12, Len: 4},
		&expr.Immediate{Register: unix.NFT_REG32_01, Data: []byte{0, 0, 0, 0}},
		&expr.Payload{DestRegister: unix.NFT_REG32_01, Base: expr.PayloadBaseTransportHeader, Offset: 0, Len: 2},
		&expr.Payload{DestRegister: unix.NFT_REG32_02, Base: expr.PayloadBaseNetworkHeader, Offset: 16, Len: 4},
		&expr.Immediate{Register: unix.NFT_REG32_03, Data: []byte{0, 0, 0, 0}},
		&expr.Payload{DestRegister: unix.NFT_REG32_03, Base: expr.PayloadBaseTransportHeader, Offset: 2, Len: 2},
	}
}
func clearMarkAndReturn() []expr.Any {
	return []expr.Any{&expr.Immediate{Register: 1, Data: []byte{0, 0, 0, 0}}, &expr.Meta{Key: expr.MetaKeyMARK, SourceRegister: true, Register: 1}, &expr.Verdict{Kind: expr.VerdictReturn}}
}
func (systemBackend) create(g *Guard) error {
	c := &nftables.Conn{}
	// Excl prevents an atomic batch from modifying a table created by
	// someone else between the ownership check and this commit.
	table := c.CreateTable(&nftables.Table{Name: g.table, Family: nftables.TableFamilyIPv4})
	set := &nftables.Set{Table: table, Name: flowSet, KeyType: nftables.MustConcatSetType(nftables.TypeIPAddr, nftables.TypeInetService, nftables.TypeIPAddr, nftables.TypeInetService), Concatenation: true, Dynamic: true, HasTimeout: true, Timeout: flowTTL, Size: 262144, Comment: g.owner}
	if e := c.AddSet(set, nil); e != nil {
		return e
	}
	priority := nftables.ChainPriority(-301)
	policy := nftables.ChainPolicyAccept
	chain := c.AddChain(&nftables.Chain{Name: "output", Table: table, Type: nftables.ChainTypeFilter, Hooknum: nftables.ChainHookOutput, Priority: &priority, Policy: &policy})
	add := func(expressions []expr.Any) {
		c.AddRule(&nftables.Rule{Table: table, Chain: chain, Exprs: expressions, UserData: userdata.AppendString(nil, userdata.TypeComment, g.owner)})
	}
	// Only marked raw packets refresh this exact address/port tuple. The
	// mark is local metadata and gets cleared before leaving this chain.
	remember := append(matchMark(g.mark), matchTCP()...)
	remember = append(remember, tuple()...)
	remember = append(remember, &expr.Dynset{SrcRegKey: unix.NFT_REG_1, SetID: set.ID, SetName: set.Name, Operation: unix.NFT_DYNSET_OP_UPDATE, Timeout: flowTTL})
	add(append(remember, clearMarkAndReturn()...))
	// Also clear the mark on ICMP and non-first fragments. Client-generated
	// RSTs already returned above; they must be delivered unchanged.
	add(append(matchMark(g.mark), clearMarkAndReturn()...))
	drop := append(matchTCP(), &expr.Payload{DestRegister: 1, Base: expr.PayloadBaseTransportHeader, Offset: 13, Len: 1}, &expr.Bitwise{SourceRegister: 1, DestRegister: 1, Len: 1, Mask: []byte{4}, Xor: []byte{0}}, &expr.Cmp{Op: expr.CmpOpNeq, Register: 1, Data: []byte{0}})
	drop = append(drop, tuple()...)
	drop = append(drop, &expr.Lookup{SourceRegister: unix.NFT_REG_1, SetID: set.ID, SetName: set.Name}, &expr.Counter{}, &expr.Verdict{Kind: expr.VerdictDrop})
	add(drop)
	if e := c.Flush(); e != nil {
		return errors.New("nftables netlink: " + e.Error())
	}
	return nil
}

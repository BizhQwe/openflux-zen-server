module openflux.local/zen/cli

go 1.26.6

require (
	golang.org/x/sys v0.48.0
	openflux.local/zen/panel v0.0.0
)

require (
	github.com/google/go-cmp v0.6.0 // indirect
	github.com/google/nftables v0.3.0 // indirect
	github.com/mdlayher/netlink v1.7.3-0.20250113171957-fbb4dce95f42 // indirect
	github.com/mdlayher/socket v0.5.0 // indirect
	golang.org/x/crypto v0.56.0 // indirect
	golang.org/x/net v0.57.0 // indirect
	golang.org/x/sync v0.6.0 // indirect
)

replace openflux.local/zen/panel => ../OpenFlux.Zen.Server.Web

replace github.com/p1neappleXpress/OpenFlux => ../OpenFlux.Zen.Server.Core

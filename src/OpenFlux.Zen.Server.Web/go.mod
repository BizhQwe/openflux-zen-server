module openflux.local/zen/panel

go 1.26.6

require (
	github.com/google/nftables v0.3.0
	github.com/p1neappleXpress/OpenFlux v0.0.0
	github.com/skip2/go-qrcode v0.0.0-20200617195104-da1b6568686e
	golang.org/x/crypto v0.56.0
	golang.org/x/sys v0.48.0
)

require (
	github.com/google/go-cmp v0.6.0 // indirect
	github.com/gorilla/websocket v1.5.3 // indirect
	github.com/klauspost/compress v1.20.0 // indirect
	github.com/mdlayher/netlink v1.7.3-0.20250113171957-fbb4dce95f42 // indirect
	github.com/mdlayher/socket v0.5.0 // indirect
	github.com/pierrec/lz4/v4 v4.1.27 // indirect
	golang.org/x/net v0.57.0 // indirect
	golang.org/x/sync v0.6.0 // indirect
)

replace github.com/p1neappleXpress/OpenFlux => ../OpenFlux.Zen.Server.Core

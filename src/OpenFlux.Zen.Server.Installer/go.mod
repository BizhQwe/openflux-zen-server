module openflux.local/zen/installer

go 1.26.6

require (
	golang.org/x/sys v0.48.0
	openflux.local/zen/panel v0.0.0
)

require golang.org/x/crypto v0.56.0 // indirect

replace openflux.local/zen/panel => ../OpenFlux.Zen.Server.Web

replace github.com/p1neappleXpress/OpenFlux => ../OpenFlux.Zen.Server.Core

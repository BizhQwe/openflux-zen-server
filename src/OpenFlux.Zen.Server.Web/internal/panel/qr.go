package panel

import (
	"github.com/skip2/go-qrcode"
)

// Render integral-sized modules with the standard four-module quiet zone.
// A small browser canvas with fractional module widths blurs dense links.
func qrPNG(link string, requested int) ([]byte, int, error) {
	qr, err := qrcode.New(link, qrcode.Low)
	if err != nil {
		return nil, 0, err
	}
	modules := len(qr.Bitmap())
	if requested <= 0 {
		requested = 1024
	}
	requested = max(256, min(2048, requested))
	scale := max(4, (requested+modules-1)/modules)
	png, err := qr.PNG(-scale)
	return png, modules, err
}

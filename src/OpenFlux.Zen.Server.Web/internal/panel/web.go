package panel

import (
	"bytes"
	"text/template"
)

// Render the embedded fragments once. Only this assembled page is public;
// template source files are never exposed by the static-file handler.
var indexPage = func() []byte {
	t := template.Must(template.New("index.html").ParseFS(assets,
		"web/html/index.html", "web/html/pages/*.html", "web/html/dialogs/*.html"))
	var page bytes.Buffer
	if err := t.ExecuteTemplate(&page, "index.html", nil); err != nil {
		panic(err)
	}
	return page.Bytes()
}()

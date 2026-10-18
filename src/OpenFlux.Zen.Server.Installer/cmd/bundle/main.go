// bundle is a build-time helper; only the core, panel and CLI are distributed.
package main

import (
	"archive/zip"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"path/filepath"
)

func main() {
	osName := flag.String("os", "", "Target OS")
	arch := flag.String("arch", "", "Target Go architecture")
	dir := flag.String("dir", "", "Directory containing the core, web and CLI binaries")
	out := flag.String("out", "", "Bundle ZIP output")
	flag.Parse()
	if *osName == "" || *arch == "" || *dir == "" || *out == "" {
		panic("all arguments are required")
	}
	suffix := ""
	if *osName == "windows" {
		suffix = ".exe"
	}
	files := map[string][]byte{}
	hashes := map[string]string{}
	for _, name := range []string{"openflux-zen-server-core" + suffix, "openflux-zen-server-web" + suffix, "openflux-zen-server-cli" + suffix} {
		b, e := os.ReadFile(filepath.Join(*dir, name))
		if e != nil {
			panic(e)
		}
		files[name] = b
		h := sha256.Sum256(b)
		hashes[name] = hex.EncodeToString(h[:])
	}
	manifest, e := json.Marshal(map[string]any{"version": "1.1.0", "os": *osName, "arch": *arch, "files": hashes})
	if e != nil {
		panic(e)
	}
	f, e := os.Create(*out)
	if e != nil {
		panic(e)
	}
	w := zip.NewWriter(f)
	for _, name := range []string{"manifest.json", "openflux-zen-server-core" + suffix, "openflux-zen-server-web" + suffix, "openflux-zen-server-cli" + suffix} {
		b := files[name]
		if name == "manifest.json" {
			b = manifest
		}
		entry, e := w.Create(name)
		if e != nil {
			panic(e)
		}
		if _, e = entry.Write(b); e != nil {
			panic(e)
		}
	}
	if e = w.Close(); e != nil {
		panic(e)
	}
	if e = f.Close(); e != nil {
		panic(e)
	}
	fmt.Printf("Bundled %s/%s\n", *osName, *arch)
}

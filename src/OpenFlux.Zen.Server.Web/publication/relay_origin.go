package publication

import (
	"context"
	"crypto/tls"
	"net/http"
	"net/url"
)

type relayOriginKey struct{}

func RelayOrigin(r *http.Request) string {
	origin, _ := r.Context().Value(relayOriginKey{}).(string)
	return origin
}

func relayHandler(handler http.Handler, origin *url.URL) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		copy := r.Clone(context.WithValue(r.Context(), relayOriginKey{}, origin.String()))
		if origin.Scheme == "https" {
			copy.TLS = &tls.ConnectionState{HandshakeComplete: true}
		}
		handler.ServeHTTP(w, copy)
	})
}

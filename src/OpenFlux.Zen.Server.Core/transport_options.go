package main

import (
	"encoding/json"
	"fmt"
	"os"
)

// applyTransportOptions lets the panel use the registry's upstream parameters
// without exposing carrier credentials in command-line arguments. Flags still
// own the process role, transport types, URLs and Session configuration.
func applyTransportOptions(specs []transportSpec, path string) error {
	if path == "" {
		return nil
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	var options map[string]map[string]interface{}
	if err := json.Unmarshal(data, &options); err != nil {
		return fmt.Errorf("read carrier parameters: %w", err)
	}
	for i := range specs {
		for key, value := range options[specs[i].Type] {
			if specs[i].Params == nil {
				specs[i].Params = make(map[string]interface{})
			}
			specs[i].Params[key] = value
		}
	}
	return nil
}

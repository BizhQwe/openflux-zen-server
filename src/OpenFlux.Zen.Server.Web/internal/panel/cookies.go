package panel

import (
	"encoding/json"
	"errors"
	"strings"
)

func ParseCookies(raw string) (map[string]string, error) {
	raw = strings.TrimSpace(raw)
	jar := map[string]string{}
	if raw == "" {
		return nil, errors.New("cookies не заданы")
	}
	var object map[string]json.RawMessage
	if json.Unmarshal([]byte(raw), &object) == nil {
		for _, name := range []string{"cookies", "rawCookies"} {
			if value, ok := object[name]; ok {
				var text string
				if json.Unmarshal(value, &text) == nil {
					return ParseCookies(text)
				}
			}
		}
		if value, ok := object["jar"]; ok {
			return ParseCookies(string(value))
		}
		for k, v := range object {
			var text string
			if json.Unmarshal(v, &text) != nil {
				return nil, errors.New("JSON cookies должен содержать строковые значения")
			}
			jar[k] = text
		}
	} else {
		var list []struct {
			Name  string `json:"name"`
			Value string `json:"value"`
		}
		if json.Unmarshal([]byte(raw), &list) == nil {
			for _, c := range list {
				if c.Name != "" {
					jar[c.Name] = c.Value
				}
			}
		} else if strings.Contains(raw, "\t") {
			for _, line := range strings.Split(raw, "\n") {
				line = strings.TrimSpace(line)
				if strings.HasPrefix(line, "#HttpOnly_") {
					line = strings.TrimPrefix(line, "#HttpOnly_")
				}
				if line == "" || strings.HasPrefix(line, "#") {
					continue
				}
				fields := strings.Split(line, "\t")
				if len(fields) >= 7 {
					jar[fields[5]] = strings.Join(fields[6:], "\t")
				}
			}
		} else {
			raw = strings.TrimPrefix(raw, "Cookie:")
			for _, item := range strings.Split(raw, ";") {
				parts := strings.SplitN(strings.TrimSpace(item), "=", 2)
				if len(parts) == 2 && parts[0] != "" {
					jar[parts[0]] = parts[1]
				}
			}
		}
	}
	if len(jar) == 0 || len(jar) > 1000 {
		return nil, errors.New("не найдены cookies или их слишком много")
	}
	for name := range jar {
		if strings.ContainsAny(name, "\r\n\x00") {
			return nil, errors.New("некорректное имя cookie")
		}
	}
	return jar, nil
}

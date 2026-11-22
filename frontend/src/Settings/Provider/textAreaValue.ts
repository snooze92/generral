// Structured values (lists, mappings) are shown as pretty-printed JSON, which is valid YAML the
// API parses back.
export function toTextAreaValue(value: unknown): unknown {
  return value !== null && typeof value === 'object'
    ? JSON.stringify(value, null, 2)
    : value;
}

# Generral specifications

These documents describe **what** Generral does and **how** it is meant to be built. They are the
source of truth until code exists; after that, they should be kept in sync with behaviour (update the
spec in the same change that alters behaviour).

## Conventions

- **MUST / SHOULD / MAY** follow [RFC 2119](https://www.rfc-editor.org/rfc/rfc2119) meanings.
- `MVP` marks features required for the first usable release; `Later` marks deferred features.
  The authoritative scope list is in [13 – Roadmap](13-roadmap.md).
- Configuration snippets are shown as **YAML** because it is readable. Generral stores
  configuration in its database and the UI edits it through forms, but every Series/Source/Mapping
  MUST also be importable/exportable as YAML (see [05 – Mapping § Import/export](05-mapping.md#importexport)).
- "Sonarr-like" means: if a Sonarr user would expect a setting, page, or behaviour to exist with a
  particular name, Generral uses the same name and the same behaviour unless a spec says otherwise.

## Reading order

1. [00 – Overview](00-overview.md) – the why.
2. [05 – Mapping](05-mapping.md) – the one genuinely new idea; everything else is plumbing.
3. [01 – Prior art](01-prior-art.md) – why we build rather than fork.
4. The rest, in numeric order.

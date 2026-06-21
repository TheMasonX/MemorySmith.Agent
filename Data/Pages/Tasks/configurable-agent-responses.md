# Configurable Agent Responses

**Priority:** Future (post-Sprint 35)
**Status:** Not started

## Overview

The agent's verbal responses (chat output) should be configurable via a wiki page or configuration file, rather than being hardcoded. This applies to:

- **Thinking responses** — e.g. "Hmm, let me think about that..."
- **Navigation responses** — e.g. "Coming!" / "On my way!"
- **Stop acknowledgements** — e.g. "Stopped." / "OK, stopping."
- **Task start/finish messages**
- **Error messages**
- **Any other hardcoded bot chat output**

## Design Sketch

A wiki page (e.g. `Data/Pages/agent-responses.md`) would contain a list of response types, each with a key and a set of predefined options the user/player can choose from. For example:

```yaml
# Response type → key → available options
responses:
  thinking:
    default: "Hmm, let me think about that..."
    options:
      - label: "Default"
        text: "Hmm, let me think about that..."
      - label: "Short"
        text: "..."
      - label: "Custom"
        text: ""  # Free-form
  comeHere:
    default: "Coming!"
    options:
      - label: "Default"
        text: "Coming!"
      - label: "Polite"
        text: "On my way!"
      - label: "Firm"
        text: "Moving."
```

## Open Questions

- Should this be a new wiki page, or part of an existing agent profile/personality config?
- How does the bot read/refresh config at runtime?
- Should the chat interpreter reference this config when composing responses?
- Should the options be limited to predefined choices, or allow fully custom text?
- Should there be a REST API endpoint to query/update the response config?

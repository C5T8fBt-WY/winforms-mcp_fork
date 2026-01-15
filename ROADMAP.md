# Roadmap

Future work and planned features for winforms-mcp.

## Planned Tools

### Event Tools (Not Yet Implemented)

The following tools were designed but not implemented. The stub methods exist in Program.cs but are currently disabled:

#### `raise_event`
Raise automation events on elements (e.g., trigger button click events programmatically).

**Planned implementation**: Use FlaUI's pattern-based event raising.

#### `listen_for_event`
Subscribe to automation events and receive notifications when they occur.

**Planned implementation**: Use FlaUI's event handlers with callback mechanism through MCP.

## Technical Debt

See [BUGS.md](./BUGS.md) for known issues and technical debt items.

## Background Automation (VDD)

The `vdd/` directory contains placeholders for a Virtual Display Driver that would enable headless sandbox automation. This would allow the sandbox to run automation even when minimized.

**Status**: Infrastructure exists, driver files need to be obtained and integrated.
**Reference**: [Microsoft IddSampleDriver](https://github.com/microsoft/Windows-driver-samples/tree/master/video/IndirectDisplay)

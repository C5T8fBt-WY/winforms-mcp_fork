# Virtual Display Driver (VDD) Placeholders

This directory contains placeholder files for the Virtual Display Driver (VDD) required for background automation in Windows Sandbox.

## Required Files

For background automation to work when the sandbox is minimized, you must replace these placeholders with actual driver files:

1. `vdd_driver.inf` - Driver installation manifest
2. `vdd_driver.sys` (or `.dll`) - Driver binary
3. `vdd_driver.cer` - Driver signing certificate

## How to Obtain

You can build the [Microsoft IddSampleDriver](https://github.com/microsoft/Windows-driver-samples/tree/master/video/IndirectDisplay) or use a third-party VDD solution.

## Deployment

When the MCP server is published, these files are copied to the output directory. The `bootstrap.ps1` script inside the sandbox will attempt to install the driver from this location.

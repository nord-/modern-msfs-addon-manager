Dev-check tools for plan 5b. They sit outside the solution and are not part of any build or test run.

`New-TestPackages.ps1 -Root <folder>` writes a Packages root into a new (or empty) folder: `Packages\Community`, `Packages\Official\Steam`, and a `Library` with `pkg-a`, `pkg-b` and `pkg-c` as Scenery Packages. `pkg-a` and `pkg-b` are linked into Community as junctions, `pkg-c` is not. The script prints the Packages root to pick in the app and the command that starts the app against the test data.

`FlightSimulator\` is a stand-in for MSFS: a console process named `FlightSimulator` that waits for Enter. Start it to make the app's sim check report a running sim, and press Enter to stop it. Build it with `dotnet build tools/dev-check/FlightSimulator`.

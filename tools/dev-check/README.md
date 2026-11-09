Dev-check tools for plan 5b. They sit outside the solution and are not part of any build or test run.

`New-TestPackages.ps1 -Root <folder>` writes a Packages root into a new (or empty) folder: `Packages\Community`, `Packages\Official\Steam`, and a `Library` with the Packages `pkg-a`, `pkg-b` and `pkg-c`. `pkg-a` and `pkg-b` are linked into Community as junctions, `pkg-c` is not. The script prints the Packages root to pick in the app, the Library folder, and the command that starts the app against the test data.

Add the Library folder under **Other** on the Welcome page. The Packages say `SCENERY` in their manifest but have no BGL files, so the app classifies them as Other; added under Scenery they show as "on but not shown" and give no cards.

`FlightSimulator\` is a stand-in for MSFS: a console process named `FlightSimulator` that waits for Enter. Start it to make the app's sim check report a running sim, and press Enter to stop it. Build it with `dotnet build tools/dev-check/FlightSimulator`.

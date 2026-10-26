# Pull request reviews

Write all review comments in English.

Priorities, in order: correctness, data safety, then maintainability. Skip formatting nits.

Project rules to enforce:

- Never move, copy or delete the user's addon files. Enabling/disabling an addon only creates or removes a directory junction in the Community folder.
- Community can contain foreign junctions, symlinks, real folders, broken links and stray files. Code must classify and leave anything it did not create untouched.
- Never remove links while MSFS is running.
- Keep MSFS 2020 specifics (paths, content formats) behind the existing abstractions so MSFS 2024 can be added later.
- The project is MIT. Flag any code that looks translated from GPL sources, such as Dave's GSX Profile Manager.
- Target .NET 10; avoid APIs that block a move to .NET 11.
- Files use CRLF line endings.

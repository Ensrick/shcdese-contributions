# Script Extender contribution checks

Selected source contributions and isolated verification for Stronghold Crusader DE Script Extender. Development sources and qualification workflows are reviewed on feature branches.

See [LICENSE.txt](LICENSE.txt) and [COPYING.GPLv3.txt](COPYING.GPLv3.txt).

## Deferred translation startup proposal

The focused [translation-startup source and controlled fixture](translation-startup/README.md)
preserve original/POST ordering while moving three translation detours to the
existing MasterController PRE phase. Its material public Apply timing change and
full-SDK/game qualification limits are explicit. The fixture is a standalone
selected-source build; it is not a complete SDK or installation package.

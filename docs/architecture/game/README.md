# Game

Source folder: `Assets/Scripts/Game`. File: `Game.asmdef`. Sub-folder: `Bootstrap`, refer to [Game/Bootstrap](bootstrap.md).

Responsibility: [Game starter](../game-starter.md).

## Purpose

`Game` is the assembly for the code of the game. It references `Core` and `UniTask`. `Core` does not reference `Game`.

## Rules

- The folder `Assets/Scripts/Game` holds only `Game.asmdef` and other folders. Do not put a script directly in it.
- Make one folder for each module, and one folder for each group of other game code. A folder name gives the namespace. For example, `Assets/Scripts/Game/Bootstrap` has the namespace `Game.Bootstrap`.
- Make one folder page for each folder. Refer to [Folders](#folders).

## Folders

| Folder | Content | Page |
|--------|---------|------|
| `Bootstrap` | `GameClient` and its hooks. | [Game/Bootstrap](bootstrap.md) |

## Folder pages

- The source folder `Assets/Scripts/Game/<Name>` has the page `docs/architecture/game/<name>.md`.
- If the folder has sub-folders, the page is `docs/architecture/game/<name>/README.md`. Each sub-folder has its own page next to it.
- Write the names in lowercase. Replace a space with a hyphen.

Each new folder page links to its responsibility pages. Add the folder page to the table above and to the tables in the [overview](../overview.md).

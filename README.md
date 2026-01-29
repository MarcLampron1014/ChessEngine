# ChessEngine
This is my attempt at making a chess engine from scratch, with the help of a lot of research.

Useful sources:
https://www.chessprogramming.org/Main_Page
https://www.chessprogramming.org/Perft_Results

## How to use the engine

### Requirements
- .NET SDK (10.0 or later)

### Build and run
- Build the project:

  dotnet build

- Run from source (debug or release configuration):

  dotnet run --project ChessEngine.csproj --configuration Debug
  dotnet run --project ChessEngine.csproj --configuration Release

- Publish a self-contained executable (example for Windows x64):

  dotnet publish -c Release -r win-x64 --self-contained true -o publish

  Then run the produced executable, for example:

  .\publish\ChessEngine.exe

### Using with a GUI (UCI)
This engine implements the UCI (Universal Chess Interface) protocol. To use the engine with a GUI (Arena, CuteChess, etc.), add it as a UCI engine and point the GUI to the executable.

Example UCI command exchange (illustrative):
- GUI -> engine: `uci`
- Engine -> GUI: `id name ChessEngine`
- Engine -> GUI: `id author marcl`
- Engine -> GUI: `uciok`
- GUI -> engine: `isready`
- Engine -> GUI: `readyok`
- GUI -> engine: `position startpos moves e2e4 e7e5`
- GUI -> engine: `go movetime 1000`
- Engine -> GUI: `bestmove e2e4`

### Manual testing
Open a terminal and run the executable; then type UCI commands like `uci`, `isready`, `position`, `go`, and `quit` to interact with the engine. On Unix-like shells you can also pipe a small script of commands into the executable for automated tests.

### Notes
- The engine currently implements a basic UCI interface and a simple time allocation heuristic.
- For development or inspection, see source files such as `Uci.cs`, `Search.cs`, `MoveGenerator.cs`, and `Perft.cs`.

---

If you want, I can add a short example script for automated testing or a sample GUI configuration for Arena/Arena-like GUIs.
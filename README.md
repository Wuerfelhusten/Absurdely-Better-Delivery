# Absurdely Better Delivery - Developer Documentation

> **Note for Users**: If you are looking for installation and usage instructions, please see [README_USER.md](README_USER.md).

## Project Overview

**Absurdely Better Delivery** is a MelonLoader mod for *Schedule I* that injects a robust delivery management system into the game. It leverages Harmony for runtime patching and Il2CppInterop for Unity engine interaction.

### Key Technical Features
- **Harmony Patching**: Non-destructive hooks into `DeliveryApp`, `DeliveryManager`, and `ShopInterface`.
- **Custom UI System**: Runtime generation of Unity UI elements (Prefabs) injected into the existing canvas.
- **State Management**: JSON-based persistence linked to save slots.
- **Network Synchronization**: Custom packet-based sync layer over Steam P2P (via FishNet/Steamworks).

## Architecture

### Directory Structure
```
src/
├── Managers/           # Core logic (History, Recurring Orders)
├── Models/             # Data structures (DeliveryRecord, RecurringSettings)
├── Multiplayer/        # Networking logic (Host/Client sync, Packets)
├── Patches/            # Harmony patches for game methods
├── Services/           # Business logic services (Repurchase, Pricing)
├── UI/                 # Unity UI generation and event handling
└── Utils/              # Helpers (Logging, Hierarchy inspection)
```

### Core Components

#### 1. Delivery History Manager
The `DeliveryHistoryManager` is the central singleton responsible for:
- Recording new deliveries via `DeliveryApp_DeliveryCompleted_Patch`.
- Serializing history to `UserData/AbsurdelyBetterDelivery/`.
- Managing the "Favorites" state.

#### 2. Recurring Order System
Implemented in `RecurringOrderService`, this system checks for due orders every second (`OnUpdate`).
- **Types**: `Daily`, `Weekly`, `OnDelivery`.
- **Logic**: Calculates `NextTriggerTime` based on game time.

#### 3. Multiplayer Synchronization
The mod uses a custom synchronization protocol to ensure host authority.
- **Transport**: Steam P2P (Steamworks).
- **Topology**: Star topology (Host <-> Clients).
- **Protocol**:
  - `FullStateSyncMessage`: Sent on join or forced sync. Contains complete history and settings.
  - `DeltaSync`: (Planned) For individual updates.
- **Entry Point**: `MultiplayerManager` detects game state (Host/Client) and initializes `HostSyncService` or `ClientSyncService`.

## Build Instructions

### Prerequisites
- **Visual Studio 2022** (or newer) with .NET 6.0 SDK.
- **MelonLoader** installed in the game directory.
- **Game Assemblies**: The project references stripped/unstripped assemblies from `Schedule I/MelonLoader/net6/`.

### Building
1.  **Clone/Open**: Open `Absurdely Better Delivery.sln`.
2.  **Restore**: Run `dotnet restore`.
3.  **Configuration**:
    - Ensure `AbsurdelyBetterDelivery.csproj` points to the correct game directory for references.
    - Check `<Reference Include="...">` paths.
4.  **Compile**: Build in **Debug** or **Release** mode.
5.  **Output**: Artifacts are generated in `bin/Debug/net6.0/`.

### Post-Build
The project includes a post-build event that automatically copies the compiled DLL to a mod directory.
- **Default Path**: Currently configured to copy to a local Vortex mods folder.
- **Customization**: You can edit the `DestinationFolder` path in `AbsurdelyBetterDelivery.csproj` to point to your game's `Mods` folder (e.g., `SteamLibrary\steamapps\common\Schedule I\Mods`) for automatic deployment during development.

## Contribution Guidelines

Pull Requests are welcome! Please ensure you adhere to the License if you want to create a fork.

### Code Style
- **Headers**: All files must include the standard GPLv3 copyright header.
- **Naming**: PascalCase for public members, _camelCase for private fields.
- **Logging**: Use `AbsurdelyBetterDeliveryMod.DebugLog()` for development logs.

### Adding New Features
1.  **UI**: Use `UIFactory` to create consistent UI elements.
2.  **Sync**: If adding state, ensure it is added to `NetworkMessages.cs` and handled in both `HostSyncService` and `ClientSyncService`.

## License

This project is licensed under the **GNU General Public License v3.0**. See [LICENSE](LICENSE) for details.

Copyright (c) 2026 Modding Forge.

# L.A.T.E - Late Access To Everyone 🚀

[![Thunderstore Version](https://img.shields.io/thunderstore/v/Nilaier/LATE?style=for-the-badge&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/repo/p/Nilaier/LATE/)
[![Thunderstore Downloads](https://img.shields.io/thunderstore/dt/Nilaier/LATE?style=for-the-badge&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/repo/p/Nilaier/LATE/)
[![Thunderstore Likes](https://img.shields.io/thunderstore/likes/Nilaier/LATE?style=for-the-badge&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/repo/p/Nilaier/LATE/)

[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.21-blue.svg?style=flat-square)](https://github.com/BepInEx/BepInEx)

This is a **purely host-sided** BepInEx mod for **R.E.P.O.** that allows players to join your game session even after it has started (late joining).

---

## ✨ Key Features

*   **Enable Late Joining:** Allows clients to connect to the host even after a level (including Shop, Truck, Arena) is in progress.
*   **Configurable Join Restrictions:** Control which scenes (Shop, Truck, Level, Arena) allow late joins via the configuration file.
*   **Comprehensive State Synchronization:** Attempts to synchronize a wide range of game states for late joiners, including:
    *   Current level progress and status (Game Over).
    *   Extraction Point status, goals, and surplus value.
    *   Valuable item values (in levels) and Shop item values (in the shop).
    *   Item States (Toggles, Battery, Mines, Melee, Drones, Grenades, Trackers, Health Packs).
    *   Destroyed objects and broken hinges/doors.
    *   Truck screen page and text initialization.
    *   Enemy Presence & State (Spawned/Despawned, Target, Freeze, Specific Behaviors - see code for full list).
    *   Arena-specific state (Cage, Winner, Pedestal).
    *   Voice chat initialization status.
*   **Spawn Location Options:**
    *   (Default) Attempts to find a safe, unoccupied spawn point.
    *   (Optional) Respawn returning players at their last known position (or death head).
*   **Death State Handling:**
    *   (Optional) Automatically re-kills late joiners if they previously died in the current level instance.
*   **Configurable Logging:** Adjust the mod's log level on the fly (Info, Debug, Warning, etc.).
*   **Advanced Option:**
    *   (Use with Caution!) Optionally force a level reload for *everyone* on late join.

---

## ⚠️ IMPORTANT: Host-Only ⚠️

**This mod only needs to be installed by the person HOSTING the game.**

Clients connecting **DO NOT** need this mod. If clients install it, it will likely have no effect or potentially cause issues. The host running the mod handles all the logic.

---

## 💾 Installation

### Automatic (Mod Manager) - Recommended

1.  Use a mod manager like [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager), [r2modman](https://github.com/ebkr/r2modmanPlus/releases/latest), or [GaleModManager](https://github.com/Krystilize/GaleModManager/releases/latest).
2.  Install this mod via the mod manager by clicking the "Install with Mod Manager" button (or similar) on the Thunderstore page.
3.  Ensure BepInEx 5.4.21 is also installed (mod managers usually handle this automatically).
4.  Launch the game via the mod manager. ✅

### Manual

1.  Ensure you have [BepInEx 5.4.21](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) installed for R.E.P.O.
2.  Download the latest release of this mod from the Thunderstore page (usually labelled "Manual Download").
3.  Extract the downloaded archive.
4.  Move the `L.A.T.E.dll` file into your `BepInEx/plugins` folder within your R.E.P.O. game directory.
5.  Launch the game normally. ✅

---

## ⚙️ Configuration

A configuration file `nilaier.late.cfg` will be generated in your `BepInEx/config` folder after running the game with the mod installed once.

You can edit this file directly using a text editor, **OR** you can use an in-game configuration editor mod for easier adjustments!

*   **Recommended:** Use [**REPOConfig** by nickklmao](https://thunderstore.io/c/repo/p/nickklmao/REPOConfig/) to edit the settings directly within the game's main menu!

The available settings are:

**[General]**

*   `Allow in shop`: (Default: `true`)
*   `Allow in truck`: (Default: `true`)
*   `Allow in level`: (Default: `true`)
*   `Allow in arena`: (Default: `true`)

**[Late Join Behavior]**

*   `Kill If Previously Dead`: (Default: `true`)
*   `Spawn At Last Position`: (Default: `true`)

**[Advanced (Use With Caution)]**

*   `Force Level Reload on Late Join`: (Default: `false`) **HIGHLY DISRUPTIVE!**

**[Debugging]**

*   `Log Level`: (Default: `Info`)

---

## 🤝 Compatibility & Testing Environment

*   This mod patches core game systems (networking, loading, spawning, items, enemies) using Harmony and MonoMod.
*   It *should* be generally compatible, but conflicts *may* arise (see below).
*   **Game Version:** Primarily developed and tested against **stable R.E.P.O. build 0.1.2**. 🎯
*   **Beta Builds (e.g., 0.1.2_21):** The open beta build introduces significant changes. L.A.T.E. has **not been tested** on the beta and is **likely to have issues**. Use on beta builds at your own risk. 🚧
*   **Sensitivity to Updates:** Relies heavily on internal code (reflection). Future game updates **will likely break this mod** until it is updated.

---

## ⛔ Known Incompatibilities

*   **Mods Tracking/Modifying Valuable Totals:**
    *   Mods like [`Map_Value_Tracker`](https://thunderstore.io/c/repo/p/Tansinator/Map_Value_Tracker/) or [`ShowTotalLoot`](https://thunderstore.io/c/repo/p/itsageba/ShowTotalLoot/) may display **incorrect values**.
    *   **Reason:** L.A.T.E.'s valuable resync can interfere with how these mods track totals.
*   **Mods Using Custom RPCs without Late-Join Handling:**
    *   May experience **desynchronization** for late joiners if the mod doesn't resend state upon player join. Compatibility depends on the other mod's design.

---

## 🤔 Known Issues

*   **Spawn/Truck Issues:** Incorrect spawn locations or falling through the floor in the Truck Lobby can occur.
*   **Extraction Point (EP) Desync:** EP may show `$0` for high-latency clients after item resync; EP might appear "Ready" but be unusable temporarily.
*   **Player State & Visual Desync:** Inventory pickup failures; dead players appearing alive; issues with specific player states (e.g., tumble); incorrect level lighting.
*   **Enemy Animation Desync:** Enemies may appear static/T-posing initially for late joiners.
*   **Performance:** Brief host-side hitch possible when syncing many items/enemies.
*   **General Desync Potential:** Complex interactions, latency, or other mods can still cause unexpected issues. `Force Level Reload` is a last resort.

---

## 🐛 Reporting Issues

If you encounter bugs, please report them on the [GitHub Issues page]([Your GitHub Repo Link]/issues) or ask in the `#mod-support` channel on the [Modding Discord]([Your Discord Invite Link]). Please include:

1.  Your `nilaier.late.cfg` file.
2.  Your BepInEx Log file (`BepInEx/LogOutput.log`).
3.  The version of R.E.P.O. you are running (e.g., `0.1.2 Stable` or `0.1.2_21 Beta`).
4.  A description of what happened.
5.  A list of other mods you are using.

---

## 🙏 Acknowledgements

*   **Semiwork:** For developing R.E.P.O.! ❤️
*   **Rebateman:** For creating the original [LateJoin mod](https://thunderstore.io/c/repo/p/Rebateman/LateJoin/) which served as a foundation and inspiration.
*   **Zehs:** For the incredibly useful [LocalMultiplayer mod](https://thunderstore.io/c/repo/p/Zehs/LocalMultiplayer/), simplifying development and testing immensely.
*   **BepInEx Team:** For the essential modding framework.
*   **HarmonyLib & MonoMod Teams:** For the powerful patching libraries.

---

## 📜 License

This mod is distributed under the **GNU General Public License v3.0**.

You can find the full license text here: [https://www.gnu.org/licenses/gpl-3.0.html](https://www.gnu.org/licenses/gpl-3.0.html)
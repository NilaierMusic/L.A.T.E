# L.A.T.E - Late Access To Everyone 🚀

<!-- Thunderstore stats -->
[![Thunderstore Version](https://img.shields.io/thunderstore/v/Nilaier/L_A_T_E?style=for-the-badge&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/repo/p/Nilaier/LATE/)
[![Thunderstore Downloads](https://img.shields.io/thunderstore/dt/Nilaier/L_A_T_E?style=for-the-badge&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/repo/p/Nilaier/LATE/)
[![Thunderstore Likes](https://img.shields.io/thunderstore/likes/Nilaier/L_A_T_E?style=for-the-badge&logo=thunderstore&logoColor=white)](https://thunderstore.io/c/repo/p/Nilaier/LATE/)

<!-- GitHub stats -->
[![GitHub Stars](https://img.shields.io/github/stars/NilaierMusic/L.A.T.E?style=for-the-badge&logo=github)](https://github.com/NilaierMusic/L.A.T.E/stargazers)
[![GitHub Forks](https://img.shields.io/github/forks/NilaierMusic/L.A.T.E?style=for-the-badge&logo=github)](https://github.com/NilaierMusic/L.A.T.E/network/members)
[![GitHub Watchers](https://img.shields.io/github/watchers/NilaierMusic/L.A.T.E?style=for-the-badge&logo=github)](https://github.com/NilaierMusic/L.A.T.E/watchers)

<!-- Framework -->
[![BepInEx](https://img.shields.io/badge/BepInEx-5.4.21-blue.svg?style=flat-square)](https://github.com/BepInEx/BepInEx)

<p align="center">
  <img src="https://raw.githubusercontent.com/NilaierMusic/L.A.T.E/main/banner.png" alt="L.A.T.E Banner" />
</p>

This is a **purely host-sided** BepInEx mod for **R.E.P.O.** that allows players to join your game session even after it has started (late joining).


## ✨ Key Features
* **Enable Late Joining:** Allows clients to connect to the host even after a level (including Shop, Truck, Arena) is in progress.
* **Configurable Join Restrictions:** Control which scenes (Shop, Truck, Level, Arena) allow late joins via the configuration file.
* **Comprehensive State Synchronization:** Attempts to synchronize a wide range of game states for late joiners, including:
  * Current level progress and status (Game Over).
  * Extraction Point status, goals, and surplus value.
  * Valuable item values (in levels) and Shop item values (in the shop).
  * Item States (Toggles, Battery, Mines, Melee, Drones, Grenades, Trackers, Health Packs).
  * Destroyed objects and broken hinges/doors.
  * Truck screen page and text initialization.
  * Enemy Presence & State (Spawned/Despawned, Target, Freeze, Specific Behaviors - see code for full list).
  * Arena-specific state (Cage, Winner, Pedestal).
  * Voice chat initialization status.
* **Spawn Location Options:**
  * (Default) Attempts to find a safe, unoccupied spawn point.
  * (Optional) Respawn returning players at their last known position (or death head).
* **Death State Handling:**  
  * (Optional) Automatically re-kills late joiners if they previously died in the current level instance.
* **Configurable Logging:** Adjust the mod's log level on the fly (Info, Debug, Warning, etc.).
* **Advanced Option:**  
  * (Use with Caution!) Optionally force a level reload for *everyone* on late join.


## ⚠️ IMPORTANT: Host-Only ⚠️
**This mod only needs to be installed by the person HOSTING the game.**  
Clients connecting **DO NOT** need this mod. If clients install it, it will likely have no effect or potentially cause issues. The host running the mod handles all the logic.


## 💾 Installation
### Automatic (Mod Manager) – Recommended
1. Use a mod manager like [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager), [r2modman](https://github.com/ebkr/r2modmanPlus/releases/latest), or [GaleModManager](https://github.com/Krystilize/GaleModManager/releases/latest).
2. Install this mod via the mod manager by clicking the “Install with Mod Manager” button (or similar) on the Thunderstore page.
3. Ensure BepInEx 5.4.21 is also installed (mod managers usually handle this automatically).
4. Launch the game via the mod manager. ✅

### Manual
1. Ensure you have [BepInEx 5.4.21](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) installed for R.E.P.O.
2. Download the latest release of this mod from the Thunderstore page (usually labelled “Manual Download”).
3. Extract the downloaded archive.
4. Move the `L.A.T.E.dll` file into your `BepInEx/plugins` folder within your R.E.P.O. game directory.
5. Launch the game normally. ✅


## ⚙️ Configuration
A configuration file `nilaier.late.cfg` will be generated in your `BepInEx/config` folder after running the game with the mod installed once.

You can edit this file directly using a text editor, **OR** you can use an in-game configuration editor mod for easier adjustments!

*Recommended:* Use [**REPOConfig** by nickklmao](https://thunderstore.io/c/repo/p/nickklmao/REPOConfig/) to edit the settings directly within the game's main menu!

<details>
<summary>Click to view all settings</summary>

### [General]
* `Allow in shop`: (Default: `true`)
* `Allow in truck`: (Default: `true`)
* `Allow in level`: (Default: `true`)
* `Allow in arena`: (Default: `true`)

### [Late Join Behavior]
* `Kill If Previously Dead`: (Default: `true`)
* `Spawn At Last Position`: (Default: `true`)

### [Advanced (Use With Caution)]
* `Force Level Reload on Late Join`: (Default: `false`) **HIGHLY DISRUPTIVE!**

### [Debugging]
* `Log Level`: (Default: `Info`)
</details>


## 🤝 Compatibility & Testing Environment
* Patches core game systems (networking, loading, spawning, items, enemies) using Harmony and MonoMod.
* **Game Version:** developed & tested on both **stable and beta R.E.P.O. builds 0.1.2/0.1.2_22beta**.
* Heavy reflection: future game updates are likely to break the mod until updated.


## ⛔ Known Incompatibilities
* **Mods Tracking/Modifying Valuable Totals**
  * e.g. [`Map_Value_Tracker`](https://thunderstore.io/c/repo/p/Tansinator/Map_Value_Tracker/) or [`ShowTotalLoot`](https://thunderstore.io/c/repo/p/itsageba/ShowTotalLoot/)
  * Values shown may be incorrect because of L.A.T.E.’s resync.
* **Mods Using Custom RPCs without Late-Join Handling**
  * Late joiners may desync if the other mod doesn’t resend state.
* **Mods that Skip Main Menu/Lobby Processes**
  * e.g. [`FastStartup`](https://thunderstore.io/c/repo/p/Kesomannen/FastStartup/)
  * Skipping essential menu and lobby steps can interfere with L.A.T.E.'s initialization and synchronization logic.
* **Mods Hooking into Level Generation or Completion with Potential Conflicts**
  * e.g. [`BerserkerEnemies`](https://thunderstore.io/c/repo/p/FNKT_Labs/BerserkerEnemies/)
    * This mod also hooks into `GenerateDone` and may cause silent NullReferenceExceptions (NREs), preventing L.A.T.E.'s necessary postfix operations from executing.
  * e.g. [`Imperium_Repo`](https://thunderstore.io/c/repo/p/giosuel/Imperium_Repo/)
    * This mod patches `LoadingLevelAnimationCompleted`, which can lead to conflicts and result in parts of Imperium's functionality not working correctly alongside L.A.T.E.
    * _Note: While L.A.T.E. has a fail-safe to prevent permanent lobby locking in such scenarios, underlying issues within these mods may still persist._


## 🤔 Known Issues
* Spawn/Truck position glitches
* Extraction Point value desync on high latency
* Inventory pickup failures, visual desync, lighting oddities
* Enemy animation “T-pose” on late join
* Brief host-side hitch when syncing many entities
* General desync potential – `Force Level Reload` is a last resort


## 🐛 Reporting Issues
Found a bug? Please open an issue on the  
[GitHub Issues page](https://github.com/NilaierMusic/L.A.T.E/issues).

Include:
1. `nilaier.late.cfg`
2. `BepInEx/LogOutput.log`
3. R.E.P.O. version (`0.1.2 Stable`, `0.1.2_22 Beta`, etc.)
4. What happened
5. Other mods in use


## 🙏 Acknowledgements
* **Semiwork** – for R.E.P.O. ❤️  
* **Rebateman** – original [LateJoin mod](https://thunderstore.io/c/repo/p/Rebateman/LateJoin/) inspiration  
* **Zehs** – [LocalMultiplayer](https://thunderstore.io/c/repo/p/Zehs/LocalMultiplayer/) for easier testing  
* **BepInEx**, **HarmonyLib**, **MonoMod** teams – frameworks & patching libraries  


## 📜 License
Distributed under the **GNU GPL v3.0**.  
See the full text here: <https://www.gnu.org/licenses/gpl-3.0.html>
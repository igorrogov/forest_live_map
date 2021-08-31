# The Forest Live Map
This is a mod for [The Forest game](https://en.wikipedia.org/wiki/The_Forest_(video_game)) based on the [Mod API](https://modapi.survivetheforest.net/). It is similar to and based on https://theforestmap.com/.

Supported features:
 * World map in a separate window (useful for multi-monitor configurations).
 * Locations on the local player and all enemies.
 * Zoomable map with mouse wheel scroll.
 * Map follows the player automatically.
 * Actual locations of certain game objects such as cave entrances, artifacts, etc. Work in progress.

The project consists of two parts:
 * The client side written in C# which interacts with the game and retrieves the required information. This is based on the [Mod API](https://modapi.survivetheforest.net/).
 * The server side written in Python ([pygame](https://www.pygame.org)) which receives data from the mod and displays objects on the screen.

TODO:
 * Make UDP port configurable. Currently it's fixed to 9999.
 * Add an option to explore the map with the mouse drag.
 * Add more game objects to the map.
 * Add UI for configuring what to display on the map.
 * Add an option to show game objects that are on the player level only (z axis). Currently the map shows all game objects even those that are located 100 meters above or below the player.

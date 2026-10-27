# Optional save-load logger

`SaveLoadExceptionLogger.cs` is separate from the UI plugin. It logs exceptions hidden by the game's existing load catch and logs thrown shelf-loader errors without swallowing them. It changes no save data and is not a general save repair.

Build separately against the matching local game, Unity, BepInEx and Harmony assemblies. Do not include temporary test probes that disable saving in a normal play installation.

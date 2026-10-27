# Optional Mac icon build callback

Copy `TCGNativeAppIcon.cs` into the Editor folder of a Unity project you may build. Supply an icon you may use at `Assets/Editor/NativeAppIcon.icns`. The callback copies it into `Contents/Resources/PlayerIcon.icns` after a macOS player build. Sign the finished bundle afterward using your existing build workflow. No icon artwork or Unity project is included here.

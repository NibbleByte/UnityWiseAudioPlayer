<img src="https://raw.githubusercontent.com/NibbleByte/UnityWiseAudioPlayer/refs/heads/master/Docs/PublishImages/Icon-160.png" width="160" align="right">

# Wise Audio Player
Simple audio framework that replaces the traditional Unity AudioSource component with a similar one that offers more features and customization.

## Why
The Unity [AudioSource](https://docs.unity3d.com/6000.5/Documentation/ScriptReference/AudioSource.html) component is very limited, and when you want to change a sound you have to hunt down all the prefabs and scenes that reference the old one. To remedy this, Unity added the [AudioRandomContainer](https://docs.unity3d.com/6000.5/Documentation/Manual/AudioRandomContainer-UI.html), which acts as an audio proxy asset that can hold a list of sounds played in a given pattern. Sadly, the UI is terrible and it doesn't give you much flexibility.

You can find a lot of audio manager and toolkit alternatives that solve these issues by placing all sound definitions in a centralized database (prefab or scriptable object) and then playing them by name string. This approach has some disadvantages:
* Having all sound definitions in one place causes conflicts when collaborating
* All sounds get loaded into memory along with the database (prefab or scriptable object)
* It needs a custom editor UI to browse and manage the sounds list, which is usually clunky
* Using strings to refer to audio assets goes against the Unity way of doing things

To avoid these disadvantages, an audio framework needs to decentralize the sound definitions into separate asset files and just use references to them directly. This is exactly what **Wise Audio Player** does.

Studios often write their own wrapper around AudioSource like this one, reinventing the wheel. Now you can just use this plugin instead.

## Usage
This audio framework revolves around the `AudioSourcePlayer` component and the `AudioPlayerAsset` scriptable object.

### AudioSourcePlayer
![AudioSourcePlayer component](https://raw.githubusercontent.com/NibbleByte/UnityWiseAudioPlayer/refs/heads/master/Docs/Screenshots/AudioSourcePlayerShot.png)

Replace all your Unity `AudioSource` components with `AudioSourcePlayer`, which gives you a similar interface to use. One important difference is that you can specify an `AudioPlayerAsset` instead of an `AudioClip` to use.

`AudioSourcePlayer` doesn't include all of the `AudioSource`'s details and curves. Instead, you can specify an `AudioSource` template prefab to copy the details from, giving you an easy way to reuse them.

"Interrupt Fade Duration" is used when the sound is manually stopped in any way, to give it a nice fade out. Even so, sounds will still stop immediately when the game object or component is destroyed.

Make sure all your sounds are played through the `AudioSourcePlayer` component or API so they're visible in the `Audio Monitor` debugging tool.

### AudioPlayerAsset
![AudioPlayerAsset scriptable object](https://raw.githubusercontent.com/NibbleByte/UnityWiseAudioPlayer/refs/heads/master/Docs/Screenshots/AudioPlayerAssetShot.png)

The `AudioPlayerAsset` serves as an audio proxy that lets you easily change what sounds should be played. Having sound definitions spread out across separate asset files makes collaborating much easier, and managing them is done through the well-known Unity Project window interface. Your programmers can link the asset in the right prefab, while your sound designer can tweak the asset itself without knowing exactly where it's played from.

The asset offers you a list of `Conductors`. Each conductor has a filter that checks whether it should be played, and only the first allowed one is used. Conductors decide what to play and how, once the player is triggered. The framework comes with conductors that cover most use cases, but if you need custom behaviour you can always extend them and make your own conductor type. Check the sample scene showcasing all conductor types. Here are the most notable conductor types:
- `PlayCollectionAudioConductor` - plays a sound picked from a list. Can play them in sequential, shuffle, or random order.
- `PlayPitchSequenceConductor` - plays the same sound every time, but changes the pitch according to your settings.
- `IntroThenLoopConductor` - plays an intro sound then loops another. Useful for music.
- `LoopSequenceOverlappingConductor` - loops sounds from a list by overlapping them (starts playing the next one before the last one finishes).

![Conductor Examples](https://raw.githubusercontent.com/NibbleByte/UnityWiseAudioPlayer/refs/heads/master/Docs/Screenshots/ConductorsShots.png)

![Conductor Examples](https://raw.githubusercontent.com/NibbleByte/UnityWiseAudioPlayer/refs/heads/master/Docs/Screenshots/ConductorFiltersShot.png)

Some conductors need to persist their state in order to work correctly. Example: `PlayPitchSequenceConductor` needs to store which pitch was last used. Conductors can store their state on the `AudioSourcePlayer` component (per audio player) or on the asset itself (per asset). You can set the location in the asset's "State Storage Location" setting.

### Audio Monitor
![Audio Monitor editor window](https://raw.githubusercontent.com/NibbleByte/UnityWiseAudioPlayer/refs/heads/master/Docs/Screenshots/AudioMonitorShot.png)

As the project grows you'll need to debug the played sounds at some point. You can use the "Audio Monitor" editor window, found at "Window / Audio / Wise Audio Monitor" in the menu. It shows what sounds were played, when, by whom, distance to the listener, and other details.

## Installation
* [Asset Store](https://u3d.as/4aZe)
* [OpenUPM](https://openupm.com/packages/devlocker.audio.wiseaudioplayer) support:
```
npm install -g openupm-cli
openupm add devlocker.audio.wiseaudioplayer
```
[![openupm](https://img.shields.io/npm/v/devlocker.audio.wiseaudioplayer?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/devlocker.audio.wiseaudioplayer/)

* GitHub UPM package - merge this into your `Packages/manifest.json`
```
{
  "dependencies": {
    "devlocker.audio.wiseaudioplayer": "https://github.com/NibbleByte/UnityWiseAudioPlayer.git#upm"
}
```

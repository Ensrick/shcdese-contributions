#nullable enable
using System;
using MonoMod.RuntimeDetour;
// These unrelated hook targets are inert compile-time/control-flow leaves.
// Their deliberately void delegates are NOT game/native ABI doubles or claims.
// The complete unmodified production Manager Apply/ApplyEarly/Unload is compiled.
namespace SHCDESE.ManagedHooks
{
    public partial class ManagedHookManager
    {
        internal delegate void gameData_getChimpGoldCost_delegate(); internal ManagedDetour<gameData_getChimpGoldCost_delegate>? gameData_getChimpGoldCost_hook; private void GameData_getChimpGoldCosts_Hook() { }
        internal delegate void engineInterface_TroopSelection_Delegate(); internal ManagedDetour<engineInterface_TroopSelection_Delegate>? engineInterface_TroopSelection_hook; private void EngineInterface_TroopSelection_Hook() { }
        internal delegate void mapFileManager_getRadarFromFileDelegate(); internal ManagedDetour<mapFileManager_getRadarFromFileDelegate>? mapFileManager_getRadarFromFile_hook; private void MapFileManager_GetRadarFromFile_Hook() { }
        internal delegate void hud_MpChatMessages_ReceiveIngameChatDelegate(); internal ManagedDetour<hud_MpChatMessages_ReceiveIngameChatDelegate>? hud_MpChatMessages_ReceiveIngameChat_hook; private void HUD_MPChatMessages_ReceiveIngameChat_Hook() { }
        internal delegate void platform_Multiplayer_ChatHandleDelegate(); internal ManagedDetour<platform_Multiplayer_ChatHandleDelegate>? platform_Multiplayer_ChatHandle_hook; private void Platform_Multiplayer_ChatHandle_Hook() { }
        internal delegate void platform_Multiplayer_SendLobbyChatMessageDelegate(); internal ManagedDetour<platform_Multiplayer_SendLobbyChatMessageDelegate>? platform_Multiplayer_SendLobbyChatMessage_hook; private void Platform_Multiplayer_SendLobbyChatMessage_Hook() { }
        internal delegate void platform_Multiplayer_SendIngameChatDelegate(); internal ManagedDetour<platform_Multiplayer_SendIngameChatDelegate>? platform_Multiplayer_SendIngameChat_hook; private void Platform_Multiplayer_SendIngameChat_Hook() { }
        internal delegate void platform_Multiplayer_ReceiveLobbyMessagesDelegate(); internal ManagedDetour<platform_Multiplayer_ReceiveLobbyMessagesDelegate>? platform_Multiplayer_receiveLobbyMessages_hook; private void Platform_Multiplayer_ReceiveLobbyMessages_Hook() { }
        internal delegate void platform_Multiplayer_SendCustomInfoToMemberDelegate(); internal ManagedDetour<platform_Multiplayer_SendCustomInfoToMemberDelegate>? platform_Multiplayer_SendCustomInfoToMember_hook; private void Platform_Multiplayer_SendCustomInfoToMember_Hook() { }
        internal delegate void fatControler_noesisGuiUpdateChecksInGameDelegate(); internal ManagedDetour<fatControler_noesisGuiUpdateChecksInGameDelegate>? fatControler_noesisGuiUpdateChecksInGame_hook; private void FatControler_NoesisGUIUpdateChecksInGame_Hook() { }
        internal delegate void HUD_Buildings_CTORDelegate(); internal ManagedDetour<HUD_Buildings_CTORDelegate>? hud_Buildings_CTOR_hook; private void HUD_Buildings_CTOR_Hook() { }
        internal delegate void GameMap_InterpChimpDelegate(); internal ManagedDetour<GameMap_InterpChimpDelegate>? gameMap_interpChimp_hook; private void GameMap_InterpChimpHook() { }
        internal delegate void GameMap_DeleteChimpDelegate(); internal ManagedDetour<GameMap_DeleteChimpDelegate>? gameMap_deleteChimp_hook; private void GameMap_DeleteChimpHook() { }
        internal delegate void myAudioManager_playSFXDelegate(); internal ManagedDetour<myAudioManager_playSFXDelegate>? myAudioManager_playSFX_hook; private void MyAudioManager_PlaySFX_Hook() { }
        internal delegate void myAudioManager_loadClipDelegate(); internal ManagedDetour<myAudioManager_loadClipDelegate>? myAudioManager_loadClip_hook; private void MyAudioManager_LoadClip_Hook() { }
        internal delegate void mapFileManager_updateWorkshopMapDelegate(); internal ManagedDetour<mapFileManager_updateWorkshopMapDelegate>? mapFileManager_updateWorkshopMap_hook; private void MapFileManager_UpdateWorkshopMap_Hook() { }
        internal delegate void SpriteMapping_SetBodySprite_delegate(); internal ManagedDetour<SpriteMapping_SetBodySprite_delegate>? spriteMapping_SetBodySprite_hook; private void SpriteMapping_SetBodySprite_Hook() { }
        internal delegate void mainViewModel_SetVisibleState_Delegate(); internal ManagedDetour<mainViewModel_SetVisibleState_Delegate>? mainViewModel_SetVisibleState_hook; private void MainViewModel_SetVisibleState_Hook() { }
        internal delegate void KeyManager_Update_Delegate(); internal ManagedDetour<KeyManager_Update_Delegate>? keyManager_Update_Hook; private void KeyManager_Update_Hook_Impl() { }
        internal delegate void customisationFileManager_ProcessExtendedLordFile_Delegate(); internal ManagedDetour<customisationFileManager_ProcessExtendedLordFile_Delegate>? customisationFileManager_ProcessExtendedLordFile_hook; private void CustomisationFileManager_ProcessExtendedLordFile_Hook() { }
        internal delegate void customisationFileManager_GetCustomLordText_Delegate(); internal ManagedDetour<customisationFileManager_GetCustomLordText_Delegate>? customisationFileManager_GetCustomLordText_hook; private void CustomisationFileManager_GetCustomLordText_Hook() { }
        internal delegate void front_Multiplayer_SkirmishAIAddClick_Delegate(); internal ManagedDetour<front_Multiplayer_SkirmishAIAddClick_Delegate>? front_Multiplayer_SkirmishAIAddClick_hook; private void FRONT_Multiplayer_SkirmishAIAddClick_Hook() { }
        internal delegate void front_Multiplayer_AILordLeave_Delegate(); internal ManagedDetour<front_Multiplayer_AILordLeave_Delegate>? front_Multiplayer_AILordLeave_hook; private void FRONT_Multiplayer_AILordLeave_Hook() { }
        internal delegate void mainViewModel_getAIFace_Delegate(); internal ManagedDetour<mainViewModel_getAIFace_Delegate>? mainViewModel_getAIFace_hook; private void MainViewModel_GetAIFace_Hook() { }
        internal delegate void noesisMediaElement_SetterDelegate(); internal ManagedDetour<noesisMediaElement_SetterDelegate>? noesisMediaElement_Setter_hook; private void NoesisMediaElement_Setter_Hook() { }
        internal delegate void SFXManager_PlaySpeech_Delegate(); internal ManagedDetour<SFXManager_PlaySpeech_Delegate>? sfxManager_PlaySpeech_hook; private void SFXManager_PlaySpeech_Hook() { }
        internal delegate void onScreenText_GetComputerName_Delegate(); internal ManagedDetour<onScreenText_GetComputerName_Delegate>? onScreenText_GetComputerName_hook; private void OnScreenText_GetComputerName_Hook() { }
        internal delegate void AIVLoader_GetAIVData_Delegate(); internal ManagedDetour<AIVLoader_GetAIVData_Delegate>? aivloader_GetAIVData_hook; private void AIVLoader_GetAIVData_Hook() { }
        internal delegate void platform_Workshop_GetListOfSubscribedItemsPaths_Delegate(); internal ManagedDetour<platform_Workshop_GetListOfSubscribedItemsPaths_Delegate>? platform_Workshop_GetlistOfSubscribedItemPaths_hook; private void Platform_Workshop_GetListOfSubscribedItemsPaths_Hook() { }
        internal delegate void frontendMenus_ClearUIPanels_Delegate(); internal ManagedDetour<frontendMenus_ClearUIPanels_Delegate>? frontendMenus_ClearUIPanels_hook; private void FrontendMenus_ClearUIPanels_Hook() { }
        internal delegate void frontendMenus_OpenFrontEndMenus_Delegate(); internal ManagedDetour<frontendMenus_OpenFrontEndMenus_Delegate>? frontendMenus_OpenFrontEndMenus_hook; private void FrontendMenus_OpenFrontEndMenus_Hook() { }
        internal delegate void GameTile_SetTileColor_Delegate(); internal ManagedDetour<GameTile_SetTileColor_Delegate>? gameTile_SetTileColor_hook; private void GameTile_SetTileColor_Hook() { }
        internal delegate void steamManager_Awake_Delegate(); internal ManagedDetour<steamManager_Awake_Delegate>? steamManager_Awake_hook; private void SteamManager_Awake_Hook() { }
        internal delegate void director_Awake(); internal ManagedDetour<director_Awake>? director_Awake_hook; private void Director_Awake_Hook() { }
        internal delegate void editorDirector_PostLoading_Delegate(); internal ManagedDetour<editorDirector_PostLoading_Delegate>? editorDirector_PostLoading_hook; private void EditorDirector_PostLoading_Hook() { }
        internal delegate void noesisXamlProvider_loadXamlDelegate(); internal ManagedDetour<noesisXamlProvider_loadXamlDelegate>? noesisXamlProvider_loadXaml_hook; private void NoesisXamlProvider_LoadXaml() { }
        internal delegate void noesisTextureProvider_GetTextureInfoDelegate(); internal ManagedDetour<noesisTextureProvider_GetTextureInfoDelegate>? noesisTextureProvider_GetTextureInfo_hook; private void NoesisTextureProvider_GetTextureInfo() { }
        internal Hook? mainViewModel_GetVersionString_hook;
        private void MainViewModel_GetVersionString_Hook() { }
    }
}
namespace CrusaderDE
{
    public sealed class GameData { public static void getChimpGoldCost() { } }
    public sealed class EngineInterface { public sealed class LoadMapReturnData { } public static void TroopSelection() { } }
    public sealed partial class MainViewModel { public string VersionString => "fixture"; public void SetVisibleState() { } public void getAIFace() { } }
    public sealed class MapFileManager { public void GetRadarFromFile() { } internal void UpdateWorkshopMap() { } }
    public sealed class HUD_MPChatMessages { public void recieveIngameChat() { } }
    public sealed class Platform_Multiplayer
    {
        internal void ChatHandle() { } public void SendLobbyChatMessage() { } public void SendIngameChat() { }
        internal void receiveLobbyMessages() { } public void SendCustomInfoToMember() { }
    }
    public sealed class FatControler { public void NoesisGUIUpdateChecksInGame() { } }
    public sealed class HUD_Buildings { }
    public sealed class Chimp { }
    public sealed class GameMap { public void interpChimp() { } public void deleteChimp(Chimp chimp, bool flag) { } }
    public sealed class MyAudioManager { public void playSFX() { } private void LoadClip(string name) { } }
    public static class SpriteMapping { public static void SetBodySprite() { } }
    public sealed class KeyManager { internal void Update() { } }
    public sealed class CustomisationFileManager { internal void ProcessExtendedLordFile() { } public void GetCustomLordText() { } }
    public sealed class FRONT_Multiplayer { public void SkirmishAIAddClick() { } public void AILordLeave() { } }
    public sealed class SFXManager { public void playSpeech() { } }
    public static class OnScreenText { public static void getComputerName() { } }
    public static class AIVLoader { public static void getAIVData() { } }
    public sealed class Platform_Workshop { public void GetListOfSubscribedItemsPaths() { } }
    public static class FrontendMenus { public static void ClearUIPanels() { } public static void OpenFrontEndMenus() { } }
    public sealed class gameTile { internal void setTileColour() { } }
    public sealed class SteamManager { internal void Awake() { } }
    public sealed class Director { internal void Awake() { } }
    public sealed class EditorDirector { public void postLoading(EngineInterface.LoadMapReturnData data, bool first, bool second) { } }
    public sealed class NoesisXamlProvider { public void LoadXaml() { } }
    public sealed class NoesisTextureProvider { public void GetTextureInfo(Uri uri) { } }
}
namespace NoesisApp
{
    public delegate void CreateMediaPlayerCallback();
    public sealed class MediaElement
    {
        public string Source { get; set; } = "";
        public static void SetCreateMediaPlayerCallback(CreateMediaPlayerCallback callback, object? state) { }
    }
}
namespace SHCDESE.ManagedHooks.Callbacks
{
    public static class NoesisCallbacks { public static void CreateMediaPlayer() { } }
}

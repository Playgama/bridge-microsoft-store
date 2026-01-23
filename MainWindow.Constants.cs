namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow
    {
        private const string SaveFileName = "save.json";

        private static class ActionName
        {
            public const string INITIALIZE = "initialize";
            public const string AUTHORIZE_PLAYER = "authorize_player";
            public const string SHARE = "share";
            public const string INVITE_FRIENDS = "invite_friends";
            public const string JOIN_COMMUNITY = "join_community";
            public const string CREATE_POST = "create_post";
            public const string ADD_TO_HOME_SCREEN = "add_to_home_screen";
            public const string ADD_TO_FAVORITES = "add_to_favorites";
            public const string RATE = "rate";
            public const string LEADERBOARDS_SET_SCORE = "leaderboards_set_score";
            public const string LEADERBOARDS_GET_ENTRIES = "leaderboards_get_entries";
            public const string LEADERBOARDS_SHOW_NATIVE_POPUP = "leaderboards_show_native_popup";
            public const string GET_PURCHASES = "get_purchases";
            public const string GET_CATALOG = "get_catalog";
            public const string PURCHASE = "purchase";
            public const string CONSUME_PURCHASE = "consume_purchase";
            public const string GET_REMOTE_CONFIG = "get_remote_config";
            public const string GET_STORAGE_DATA = "get_storage_data";
            public const string SET_STORAGE_DATA = "set_storage_data";
            public const string DELETE_STORAGE_DATA = "delete_storage_data";
            public const string CLIPBOARD_WRITE = "clipboard_write";
            public const string ADBLOCK_DETECT = "adblock_detect";
            public const string SET_INTERSTITIAL_STATE = "set_interstitial_state";
            public const string SET_REWARDED_STATE = "set_rewarded_state";
            public const string SHOW_INTERSTITIAL = "show_interstitial";
            public const string SHOW_REWARDED = "show_rewarded";
        }

        private static class LocalSettingKey
        {
            public const string LaunchCount = "launchCount";
            public const string RateDialogEverShown = "rateDialogEverShown";
        }

        private const int MinLaunchCountToPrompt = 3;
    }
}
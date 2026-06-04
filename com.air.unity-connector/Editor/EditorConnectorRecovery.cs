namespace Air.UnityConnector
{
    /// <summary>Public entry for game projects to recover Editor HTTP without referencing internal supervisor types.</summary>
    public static class EditorConnectorRecovery
    {
        public static void RestartEditorHttp() => EditorConnectorBootstrap.RequestControlledRestart();
    }
}

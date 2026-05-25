namespace UnityCliConnector.Builtin
{
    [CliCommand(
        "editor.manage",
        Scope = CommandScope.Editor,
        Description = "Editor control: play, stop, pause, refresh, tags, layers, tools",
        Aliases = "manage")]
    public static class ManageEditorCommand
    {
        public static CommandResult Run(CliParams p)
        {
            try
            {
                return CommandResult.Success(Editor.Services.EditorManageService.Execute(p.ToDictionary()));
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(ex.Message);
            }
        }
    }
}

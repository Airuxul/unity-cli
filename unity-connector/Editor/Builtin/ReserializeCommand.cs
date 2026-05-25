namespace UnityCliConnector.Builtin
{
    [CliCommand(
        "editor.reserialize",
        Scope = CommandScope.Editor,
        Description = "Force reserialize assets (whole project or paths)",
        Aliases = "reserialize")]
    public static class ReserializeCommand
    {
        public static CommandResult Run(CliParams p)
        {
            try
            {
                return CommandResult.Success(Editor.Services.ReserializeService.Reserialize(p));
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(ex.Message);
            }
        }
    }
}

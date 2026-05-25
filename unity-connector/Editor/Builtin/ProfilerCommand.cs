namespace UnityCliConnector.Builtin
{
    [CliCommand(
        "editor.profiler",
        Scope = CommandScope.Editor,
        Description = "Unity Profiler: hierarchy, enable, disable, status, clear",
        Aliases = "profiler")]
    public static class ProfilerCommand
    {
        public static CommandResult Run(CliParams p)
        {
            try
            {
                return CommandResult.Success(Editor.Services.ProfilerHierarchyService.Execute(p));
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(ex.Message);
            }
        }
    }
}

namespace UnityCliConnector.Builtin
{
    [CliCommand(
        "editor.exec",
        Scope = CommandScope.Editor,
        Description = "Compile and execute arbitrary C# in Editor context",
        Aliases = "exec")]
    public static class ExecCommand
    {
        public static CommandResult Run(CliParams p)
        {
            try
            {
                return CommandResult.Success(Editor.Services.CSharpExecutor.Execute(p));
            }
            catch (System.Exception ex)
            {
                return CommandResult.Fail(ex.Message);
            }
        }
    }
}

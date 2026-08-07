# Samples

One directory per sample application. Each is a self-contained host: it builds the ten
standard plugins into a `plugins` folder beside its executable, loads them by scanning
that folder, and compiles a rule set from its own `RuleSets` directory. None of them names
a plugin type, so each shows the same discovery path a deployed application takes.

| | |
| --- | --- |
| [`Reversi/`](Reversi/) | a playable Reversi in the terminal — `dotnet run --project sample/Reversi -- --auto` |

## Adding a sample

Create `sample/<Name>/` with a `Rulealize.Sample.<Name>.csproj` alongside a `RuleSets`
directory, then add it to [`Rulealize.slnx`](../Rulealize.slnx) under the `sample` folder
and to the table above. The project file needs three things beyond the usual:

```xml
<ProjectReference Include="..\..\src\Rulealize.csproj" />
<Import Project="..\..\StandardPlugins.props" />
<Content Include="RuleSets\*.json" CopyToOutputDirectory="PreserveNewest" />
```

`StandardPlugins.props` locates the plugin repositories relative to itself, so it needs no
adjusting for the extra directory level.

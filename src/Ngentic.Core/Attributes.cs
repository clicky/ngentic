namespace Ngentic;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpDependencyAttribute : Attribute
{
    public string Name { get; }
    public bool Required { get; init; } = true;

    public McpDependencyAttribute(string name)
    {
        Name = name;
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ModelAttribute : Attribute
{
    public string Id { get; }
    public ModelAttribute(string id) { Id = id; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class MaxTurnsAttribute : Attribute
{
    public int Value { get; }
    public MaxTurnsAttribute(int value) { Value = value; }
}

using MapLibre.Unity.Style;

namespace MapLibre.Unity.Source
{
    public interface ISource
    {
        string Id { get; }
        SourceDefinition Definition { get; }
        void Dispose();
    }
}

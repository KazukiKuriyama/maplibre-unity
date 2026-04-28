using System.Runtime.CompilerServices;

// Expose internal types to the test assemblies so EditMode unit tests can
// validate helpers (e.g. ColorSpaceConversion) without forcing them public.
// Add additional names here if a future test assembly needs the same access.
[assembly: InternalsVisibleTo("MapLibre.Unity.Tests.EditMode")]
[assembly: InternalsVisibleTo("MapLibre.Unity.Tests.PlayMode")]

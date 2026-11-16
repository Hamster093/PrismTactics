using System.Collections.Generic;
public class AOTGenericReferences : UnityEngine.MonoBehaviour
{

    // {{ AOT assemblies
    public static readonly IReadOnlyList<string> PatchedAOTAssemblyList = new List<string>
    {
        "mscorlib.dll",
    };
    // }}

    // {{ constraint implement type
    // }} 

    // {{ AOT generic types
    // System.Collections.Generic.IReadOnlyCollection<object>
    // System.Collections.Generic.IReadOnlyList<object>
    // }}

    public void RefMethods()
    {
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MonoMod.RuntimeDetour {
#if !MONOMOD_INTERNAL
    public
#endif
    interface IGenericDetourPlatform {
        // there will be an implementation of this for each native/runtime combination
        int AddPatch(MethodBase from, MethodInfo to);
        void RemovePatch(int handle);
    }
}

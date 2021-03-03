using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MonoMod.Common.RuntimeDetour {
#if !MONOMOD_INTERNAL
    public
#endif
    interface IGenericDetourPlatform {
        // there will be an implementation of this for each native/runtime combination
    }
}

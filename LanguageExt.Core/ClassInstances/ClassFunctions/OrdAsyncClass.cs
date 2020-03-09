using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LanguageExt.Common;
using LanguageExt.TypeClasses;
using static LanguageExt.Prelude;

namespace LanguageExt.ClassInstances
{
    public static class OrdAsyncClass
    {
        public static Task<int> CompareAsync<A>(A x, A y) => OrdAsyncClass<A>.CompareAsync(x, y);
        public static Task<bool> EqualsAsync<A>(A x, A y) => OrdAsyncClass<A>.EqualsAsync(x, y);
        public static Task<int> GetHashCodeAsync<A>(A x, A y) => OrdAsyncClass<A>.GetHashCodeAsync(x);
    }

    public static class OrdAsyncClass<A>
    {
        public static readonly Option<Error> Error;
        public static readonly Func<A, A, Task<int>> CompareAsync;
        public static readonly Func<A, A, Task<bool>> EqualsAsync = EqAsyncClass<A>.EqualsAsync;
        public static readonly Func<A, Task<int>> GetHashCodeAsync = HashableAsyncClass<A>.GetHashCodeAsync;
        
        static OrdAsyncClass()
        {
            try
            {
                var (fullName, name, gens) = ClassFunctions.GetTypeInfo<A>();
                CompareAsync = ClassFunctions.MakeFunc2<A, A, Task<int>>(name, "CompareAsync", gens, 
                    ("Ord", "Async"), 
                    ("Ord", ""));
            }
            catch (Exception e)
            {
                Error = Some(Common.Error.New(e));
            }
        }
    }
}
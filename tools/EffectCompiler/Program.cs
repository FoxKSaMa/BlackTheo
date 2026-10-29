using System.Runtime.InteropServices;

namespace BlackTheo.EffectCompiler;

/// <summary>
/// A tiny fxc replacement built on the D3DX9 effect compiler.
///
/// FNA's Effect class consumes FX9 effect bytecode (the same format produced
/// by `fxc.exe /T fx_2_0 BlackHole.fx /Fo BlackHole.cso`). The Windows SDK is
/// not installed in this environment, but d3dx9_43.dll ships with Windows and
/// exposes D3DXCreateEffectCompiler, which is exactly what fxc used under the
/// hood. This tool calls it through COM and writes the resulting bytecode.
/// </summary>
internal static class Program
{
    private delegate int CompileEffectDelegate(IntPtr self, uint flags, out IntPtr effect, out IntPtr errors);
    private delegate uint ReleaseDelegate(IntPtr self);
    private delegate IntPtr GetBufferPointerDelegate(IntPtr self);

    [DllImport("d3dx9_43.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DXCreateEffectCompiler(
        byte[] sourceData,
        uint sourceDataLength,
        IntPtr defines,
        IntPtr include,
        uint flags,
        out IntPtr compiler,
        out IntPtr parseErrors);

    private const int CompileEffectVTableIndex = 3 + 54 + 2; // IUnknown + ID3DXBaseEffect + SetLiteral + GetLiteral
    private const int ReleaseVTableIndex = 2;
    private const int GetBufferPointerVTableIndex = 3;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: EffectCompiler <input.fx> <output.cso>");
            return 2;
        }

        string input = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        byte[] source = File.ReadAllBytes(input);

        IntPtr compiler = IntPtr.Zero;
        IntPtr parseErrors = IntPtr.Zero;
        try
        {
            int hr = D3DXCreateEffectCompiler(source, (uint)source.Length, IntPtr.Zero, IntPtr.Zero, 0, out compiler, out parseErrors);
            if (hr < 0)
            {
                Console.Error.WriteLine($"D3DXCreateEffectCompiler failed: 0x{hr:X8}");
                DumpErrors(parseErrors);
                return 1;
            }

            var compile = GetVtableDelegate<CompileEffectDelegate>(compiler, CompileEffectVTableIndex);
            IntPtr effect = IntPtr.Zero;
            IntPtr errors = IntPtr.Zero;
            try
            {
                hr = compile(compiler, 0, out effect, out errors);
                if (hr < 0)
                {
                    Console.Error.WriteLine($"CompileEffect failed: 0x{hr:X8}");
                    DumpErrors(errors);
                    return 1;
                }

                var getPointer = GetVtableDelegate<GetBufferPointerDelegate>(effect, GetBufferPointerVTableIndex);
                IntPtr pointer = getPointer(effect);
                if (pointer == IntPtr.Zero)
                {
                    Console.Error.WriteLine("CompileEffect returned a null bytecode buffer.");
                    return 1;
                }

                var getSize = GetVtableDelegate<BufferSizeDelegate>(effect, 4);
                uint size = getSize(effect);
                byte[] bytecode = new byte[size];
                Marshal.Copy(pointer, bytecode, 0, bytecode.Length);

                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllBytes(output, bytecode);
                Console.WriteLine($"Compiled {input} -> {output} ({bytecode.Length} bytes)");
                return 0;
            }
            finally
            {
                if (errors != IntPtr.Zero)
                    Release(errors);
                if (effect != IntPtr.Zero)
                    Release(effect);
            }
        }
        finally
        {
            if (parseErrors != IntPtr.Zero)
                Release(parseErrors);
            if (compiler != IntPtr.Zero)
                Release(compiler);
        }
    }

    private delegate uint BufferSizeDelegate(IntPtr self);

    private static T GetVtableDelegate<T>(IntPtr comObject, int index) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        IntPtr fn = Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    private static void Release(IntPtr comObject)
    {
        try
        {
            var release = GetVtableDelegate<ReleaseDelegate>(comObject, ReleaseVTableIndex);
            release(comObject);
        }
        catch
        {
            // Nothing useful to do while unwinding a failed compilation.
        }
    }

    private static void DumpErrors(IntPtr errors)
    {
        if (errors == IntPtr.Zero)
            return;

        try
        {
            var getPointer = GetVtableDelegate<GetBufferPointerDelegate>(errors, GetBufferPointerVTableIndex);
            var getSize = GetVtableDelegate<BufferSizeDelegate>(errors, 4);
            IntPtr pointer = getPointer(errors);
            uint size = getSize(errors);
            if (pointer != IntPtr.Zero && size > 0)
                Console.Error.WriteLine(Marshal.PtrToStringAnsi(pointer, (int)size));
        }
        catch
        {
            // Ignore errors while reporting errors.
        }
    }
}

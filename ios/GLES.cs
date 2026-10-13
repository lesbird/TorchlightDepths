using System;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;

// iOS stand-in for OpenTK's OpenGL4 bindings, covering exactly the calls the game makes.
//
// OpenTK's generated GL class can't run on iOS: its static constructor creates
// native→managed delegate thunks, which the ahead-of-time-only runtime can't JIT.
// These are plain P/Invokes into Apple's OpenGLES framework instead. Every function
// used exists in OpenGL ES 3.0 with the same name and enum values as desktop GL, so
// the shared rendering code compiles against this unchanged.
namespace OpenTK.Graphics.OpenGL4
{
    public enum EnableCap { Blend = 0x0BE2, DepthTest = 0x0B71 }
    public enum BlendingFactor { SrcAlpha = 0x0302, OneMinusSrcAlpha = 0x0303 }
    [Flags] public enum ClearBufferMask { DepthBufferBit = 0x0100, ColorBufferBit = 0x4000 }
    public enum TextureTarget { Texture2D = 0x0DE1 }
    public enum GenerateMipmapTarget { Texture2D = 0x0DE1 }
    public enum TextureUnit { Texture0 = 0x84C0, Texture1 = 0x84C1, Texture2 = 0x84C2, Texture3 = 0x84C3 }
    public enum PixelInternalFormat { Rgba = 0x1908 }
    public enum PixelFormat { Rgba = 0x1908 }
    public enum PixelType { UnsignedByte = 0x1401 }
    public enum PixelStoreParameter { UnpackAlignment = 0x0CF5, PackAlignment = 0x0D05 }
    public enum TextureMinFilter
    {
        Nearest = 0x2600, Linear = 0x2601,
        NearestMipmapNearest = 0x2700, LinearMipmapNearest = 0x2701,
        NearestMipmapLinear = 0x2702, LinearMipmapLinear = 0x2703,
    }
    public enum TextureMagFilter { Nearest = 0x2600, Linear = 0x2601 }
    public enum TextureParameterName { TextureMagFilter = 0x2800, TextureMinFilter = 0x2801, TextureWrapS = 0x2802, TextureWrapT = 0x2803 }
    public enum TextureWrapMode { Repeat = 0x2901, ClampToEdge = 0x812F }
    public enum ShaderType { FragmentShader = 0x8B30, VertexShader = 0x8B31 }
    public enum ShaderParameter { CompileStatus = 0x8B81, InfoLogLength = 0x8B84 }
    public enum GetProgramParameterName { LinkStatus = 0x8B82, InfoLogLength = 0x8B84 }
    public enum BufferTarget { ArrayBuffer = 0x8892 }
    public enum BufferUsageHint { StaticDraw = 0x88E4, DynamicDraw = 0x88E8 }
    public enum VertexAttribPointerType { Float = 0x1406 }
    public enum PrimitiveType { Triangles = 0x0004 }

    public static unsafe class GL
    {
        const string Lib = "/System/Library/Frameworks/OpenGLES.framework/OpenGLES";

        [DllImport(Lib)] static extern void glEnable(int cap);
        [DllImport(Lib)] static extern void glDisable(int cap);
        [DllImport(Lib)] static extern void glBlendFunc(int s, int d);
        [DllImport(Lib)] static extern void glClearColor(float r, float g, float b, float a);
        [DllImport(Lib)] static extern void glClear(int mask);
        [DllImport(Lib)] static extern void glViewport(int x, int y, int w, int h);
        [DllImport(Lib)] static extern void glFinish();
        [DllImport(Lib)] static extern void glPixelStorei(int pname, int param);
        [DllImport(Lib)] static extern void glReadPixels(int x, int y, int w, int h, int format, int type, void* data);

        [DllImport(Lib)] static extern int glCreateShader(int type);
        [DllImport(Lib)] static extern void glShaderSource(int shader, int count, byte** str, int* len);
        [DllImport(Lib)] static extern void glCompileShader(int shader);
        [DllImport(Lib)] static extern void glGetShaderiv(int shader, int pname, int* p);
        [DllImport(Lib)] static extern void glGetShaderInfoLog(int shader, int max, int* len, byte* log);
        [DllImport(Lib)] static extern void glDeleteShader(int shader);
        [DllImport(Lib)] static extern int glCreateProgram();
        [DllImport(Lib)] static extern void glAttachShader(int prog, int shader);
        [DllImport(Lib)] static extern void glDetachShader(int prog, int shader);
        [DllImport(Lib)] static extern void glLinkProgram(int prog);
        [DllImport(Lib)] static extern void glGetProgramiv(int prog, int pname, int* p);
        [DllImport(Lib)] static extern void glGetProgramInfoLog(int prog, int max, int* len, byte* log);
        [DllImport(Lib)] static extern void glUseProgram(int prog);
        [DllImport(Lib)] static extern void glDeleteProgram(int prog);
        [DllImport(Lib)] static extern int glGetUniformLocation(int prog, byte* name);
        [DllImport(Lib)] static extern void glUniform1i(int loc, int v);
        [DllImport(Lib)] static extern void glUniform1f(int loc, float v);
        [DllImport(Lib)] static extern void glUniform2f(int loc, float x, float y);
        [DllImport(Lib)] static extern void glUniform3f(int loc, float x, float y, float z);
        [DllImport(Lib)] static extern void glUniform4f(int loc, float x, float y, float z, float w);
        [DllImport(Lib)] static extern void glUniformMatrix4fv(int loc, int count, byte transpose, float* m);

        [DllImport(Lib)] static extern void glGenVertexArrays(int n, int* ids);
        [DllImport(Lib)] static extern void glDeleteVertexArrays(int n, int* ids);
        [DllImport(Lib)] static extern void glBindVertexArray(int id);
        [DllImport(Lib)] static extern void glGenBuffers(int n, int* ids);
        [DllImport(Lib)] static extern void glDeleteBuffers(int n, int* ids);
        [DllImport(Lib)] static extern void glBindBuffer(int target, int id);
        [DllImport(Lib)] static extern void glBufferData(int target, nint size, void* data, int usage);
        [DllImport(Lib)] static extern void glEnableVertexAttribArray(int index);
        [DllImport(Lib)] static extern void glVertexAttribPointer(int index, int size, int type, byte norm, int stride, nint offset);
        [DllImport(Lib)] static extern void glDrawArrays(int mode, int first, int count);

        [DllImport(Lib)] static extern void glGenTextures(int n, int* ids);
        [DllImport(Lib)] static extern void glDeleteTextures(int n, int* ids);
        [DllImport(Lib)] static extern void glBindTexture(int target, int id);
        [DllImport(Lib)] static extern void glActiveTexture(int unit);
        [DllImport(Lib)] static extern void glTexImage2D(int target, int level, int ifmt, int w, int h, int border, int fmt, int type, void* data);
        [DllImport(Lib)] static extern void glTexParameteri(int target, int pname, int param);
        [DllImport(Lib)] static extern void glGenerateMipmap(int target);

        // ---- state ----------------------------------------------------------
        public static void Enable(EnableCap c) => glEnable((int)c);
        public static void Disable(EnableCap c) => glDisable((int)c);
        public static void BlendFunc(BlendingFactor s, BlendingFactor d) => glBlendFunc((int)s, (int)d);
        public static void ClearColor(float r, float g, float b, float a) => glClearColor(r, g, b, a);
        public static void Clear(ClearBufferMask m) => glClear((int)m);
        public static void Viewport(int x, int y, int w, int h) => glViewport(x, y, w, h);
        public static void Finish() => glFinish();
        public static void PixelStore(PixelStoreParameter p, int v) => glPixelStorei((int)p, v);
        public static void ReadPixels(int x, int y, int w, int h, PixelFormat f, PixelType t, byte[] data)
        {
            fixed (byte* p = data) glReadPixels(x, y, w, h, (int)f, (int)t, p);
        }

        // ---- shaders / programs ---------------------------------------------
        public static int CreateShader(ShaderType t) => glCreateShader((int)t);
        public static void ShaderSource(int shader, string src)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(src);
            fixed (byte* p = utf8)
            {
                byte* s = p;
                int len = utf8.Length;
                glShaderSource(shader, 1, &s, &len);
            }
        }
        public static void CompileShader(int s) => glCompileShader(s);
        public static void GetShader(int s, ShaderParameter p, out int v) { int r; glGetShaderiv(s, (int)p, &r); v = r; }
        public static string GetShaderInfoLog(int s)
        {
            GetShader(s, ShaderParameter.InfoLogLength, out int n);
            if (n <= 0) return "";
            var buf = new byte[n];
            fixed (byte* p = buf) glGetShaderInfoLog(s, n, null, p);
            return Encoding.UTF8.GetString(buf).TrimEnd('\0');
        }
        public static void DeleteShader(int s) => glDeleteShader(s);
        public static int CreateProgram() => glCreateProgram();
        public static void AttachShader(int p, int s) => glAttachShader(p, s);
        public static void DetachShader(int p, int s) => glDetachShader(p, s);
        public static void LinkProgram(int p) => glLinkProgram(p);
        public static void GetProgram(int p, GetProgramParameterName n, out int v) { int r; glGetProgramiv(p, (int)n, &r); v = r; }
        public static string GetProgramInfoLog(int p)
        {
            GetProgram(p, GetProgramParameterName.InfoLogLength, out int n);
            if (n <= 0) return "";
            var buf = new byte[n];
            fixed (byte* b = buf) glGetProgramInfoLog(p, n, null, b);
            return Encoding.UTF8.GetString(buf).TrimEnd('\0');
        }
        public static void UseProgram(int p) => glUseProgram(p);
        public static void DeleteProgram(int p) => glDeleteProgram(p);
        public static int GetUniformLocation(int p, string name)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(name + "\0");
            fixed (byte* n = utf8) return glGetUniformLocation(p, n);
        }
        public static void Uniform1(int loc, int v) => glUniform1i(loc, v);
        public static void Uniform1(int loc, float v) => glUniform1f(loc, v);
        public static void Uniform2(int loc, Vector2 v) => glUniform2f(loc, v.X, v.Y);
        public static void Uniform3(int loc, Vector3 v) => glUniform3f(loc, v.X, v.Y, v.Z);
        public static void Uniform4(int loc, Vector4 v) => glUniform4f(loc, v.X, v.Y, v.Z, v.W);
        public static void UniformMatrix4(int loc, bool transpose, ref Matrix4 m)
        {
            fixed (Matrix4* p = &m) glUniformMatrix4fv(loc, 1, (byte)(transpose ? 1 : 0), (float*)p);
        }

        // ---- buffers / vertex arrays ----------------------------------------
        public static int GenVertexArray() { int id; glGenVertexArrays(1, &id); return id; }
        public static void DeleteVertexArray(int id) => glDeleteVertexArrays(1, &id);
        public static void BindVertexArray(int id) => glBindVertexArray(id);
        public static int GenBuffer() { int id; glGenBuffers(1, &id); return id; }
        public static void DeleteBuffer(int id) => glDeleteBuffers(1, &id);
        public static void BindBuffer(BufferTarget t, int id) => glBindBuffer((int)t, id);
        public static void BufferData(BufferTarget t, int size, float[] data, BufferUsageHint u)
        {
            fixed (float* p = data) glBufferData((int)t, size, p, (int)u);
        }
        public static void EnableVertexAttribArray(int i) => glEnableVertexAttribArray(i);
        public static void VertexAttribPointer(int i, int size, VertexAttribPointerType t, bool norm, int stride, int offset)
            => glVertexAttribPointer(i, size, (int)t, (byte)(norm ? 1 : 0), stride, offset);
        public static void DrawArrays(PrimitiveType m, int first, int count) => glDrawArrays((int)m, first, count);

        // ---- textures --------------------------------------------------------
        public static int GenTexture() { int id; glGenTextures(1, &id); return id; }
        public static void DeleteTexture(int id) => glDeleteTextures(1, &id);
        public static void BindTexture(TextureTarget t, int id) => glBindTexture((int)t, id);
        public static void ActiveTexture(TextureUnit u) => glActiveTexture((int)u);
        public static void TexImage2D(TextureTarget t, int level, PixelInternalFormat ifmt, int w, int h, int border,
            PixelFormat fmt, PixelType type, byte[] data)
        {
            fixed (byte* p = data) glTexImage2D((int)t, level, (int)ifmt, w, h, border, (int)fmt, (int)type, p);
        }
        public static void TexParameter(TextureTarget t, TextureParameterName n, int v) => glTexParameteri((int)t, (int)n, v);
        public static void GenerateMipmap(GenerateMipmapTarget t) => glGenerateMipmap((int)t);
    }
}

using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Dungeon
{
    /// <summary>Compiled shader program with a tiny uniform helper.</summary>
    public sealed class Shader : IDisposable
    {
        public readonly int Handle;

        public Shader(string vertexSrc, string fragmentSrc)
        {
            int vs = Compile(ShaderType.VertexShader, vertexSrc);
            int fs = Compile(ShaderType.FragmentShader, fragmentSrc);
            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vs);
            GL.AttachShader(Handle, fs);
            GL.LinkProgram(Handle);
            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
                throw new Exception("Program link error: " + GL.GetProgramInfoLog(Handle));
            GL.DetachShader(Handle, vs);
            GL.DetachShader(Handle, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        static int Compile(ShaderType type, string src)
        {
            int id = GL.CreateShader(type);
            GL.ShaderSource(id, src);
            GL.CompileShader(id);
            GL.GetShader(id, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
                throw new Exception($"{type} compile error: " + GL.GetShaderInfoLog(id));
            return id;
        }

        public void Use() => GL.UseProgram(Handle);
        int Loc(string name) => GL.GetUniformLocation(Handle, name);

        public void Set(string n, int v) => GL.Uniform1(Loc(n), v);
        public void Set(string n, float v) => GL.Uniform1(Loc(n), v);
        public void Set(string n, Vector2 v) => GL.Uniform2(Loc(n), v);
        public void Set(string n, Vector3 v) => GL.Uniform3(Loc(n), v);
        public void Set(string n, Vector4 v) => GL.Uniform4(Loc(n), v);
        public void Set(string n, Matrix4 m) { var loc = Loc(n); GL.UniformMatrix4(loc, true, ref m); }

        public void Dispose() => GL.DeleteProgram(Handle);
    }

    /// <summary>Static mesh: interleaved position(3) / normal(3) / uv(2).</summary>
    public sealed class Mesh : IDisposable
    {
        readonly int _vao, _vbo, _count;

        public Mesh(float[] verts)
        {
            _count = verts.Length / 8;
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
            int stride = 8 * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.BindVertexArray(0);
        }

        public void Draw()
        {
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _count);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
        }

        // ---- factory helpers -------------------------------------------------

        /// <summary>Unit cube spanning [0,1]^3, outward normals, per-face UV 0..1.</summary>
        public static Mesh Cube()
        {
            // face = 4 corners (ccw) + normal + per-corner uv
            void Quad(System.Collections.Generic.List<float> l, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                void V(Vector3 p, float u, float v) { l.Add(p.X); l.Add(p.Y); l.Add(p.Z); l.Add(n.X); l.Add(n.Y); l.Add(n.Z); l.Add(u); l.Add(v); }
                V(a, 0, 0); V(b, 1, 0); V(c, 1, 1);
                V(a, 0, 0); V(c, 1, 1); V(d, 0, 1);
            }
            var l = new System.Collections.Generic.List<float>();
            Vector3 o = Vector3.Zero;
            // corners
            Vector3 p000 = new(0, 0, 0), p100 = new(1, 0, 0), p110 = new(1, 1, 0), p010 = new(0, 1, 0);
            Vector3 p001 = new(0, 0, 1), p101 = new(1, 0, 1), p111 = new(1, 1, 1), p011 = new(0, 1, 1);
            Quad(l, p001, p101, p111, p011, new(0, 0, 1));   // +Z
            Quad(l, p100, p000, p010, p110, new(0, 0, -1));  // -Z
            Quad(l, p101, p100, p110, p111, new(1, 0, 0));   // +X
            Quad(l, p000, p001, p011, p010, new(-1, 0, 0));  // -X
            Quad(l, p010, p011, p111, p110, new(0, 1, 0));   // +Y
            Quad(l, p001, p000, p100, p101, new(0, -1, 0));  // -Y
            return new Mesh(l.ToArray());
        }

        /// <summary>1x1 floor tile in XZ plane at y=0, normal +Y.</summary>
        public static Mesh FloorTile()
        {
            float[] v =
            {
                0,0,0, 0,1,0, 0,0,
                1,0,0, 0,1,0, 1,0,
                1,0,1, 0,1,0, 1,1,
                0,0,0, 0,1,0, 0,0,
                1,0,1, 0,1,0, 1,1,
                0,0,1, 0,1,0, 0,1,
            };
            return new Mesh(v);
        }

        /// <summary>1x1 ceiling tile in XZ plane at y=0 (translate to y=1), normal -Y.</summary>
        public static Mesh CeilTile()
        {
            float[] v =
            {
                0,0,0, 0,-1,0, 0,0,
                1,0,1, 0,-1,0, 1,1,
                1,0,0, 0,-1,0, 1,0,
                0,0,0, 0,-1,0, 0,0,
                0,0,1, 0,-1,0, 0,1,
                1,0,1, 0,-1,0, 1,1,
            };
            return new Mesh(v);
        }

        /// <summary>Upright billboard quad, x in [-0.45,0.45], y in [0.02,0.92], z=0, normal +Z.</summary>
        public static Mesh Billboard()
        {
            float xl = -0.45f, xr = 0.45f, yb = 0.02f, yt = 0.92f;
            float[] v =
            {
                xl,yb,0, 0,0,1, 0,1,
                xr,yb,0, 0,0,1, 1,1,
                xr,yt,0, 0,0,1, 1,0,
                xl,yb,0, 0,0,1, 0,1,
                xr,yt,0, 0,0,1, 1,0,
                xl,yt,0, 0,0,1, 0,0,
            };
            return new Mesh(v);
        }
    }

    /// <summary>RGBA texture object.</summary>
    public sealed class GlTexture : IDisposable
    {
        public readonly int Handle;

        public GlTexture(byte[] rgba, int w, int h, bool nearest, bool mipmap)
        {
            Handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, Handle);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, w, h, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            var min = mipmap ? (nearest ? TextureMinFilter.NearestMipmapLinear : TextureMinFilter.LinearMipmapLinear)
                             : (nearest ? TextureMinFilter.Nearest : TextureMinFilter.Linear);
            var mag = nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)min);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)mag);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            if (mipmap) GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        }

        public void Bind(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, Handle);
        }

        public void Dispose() => GL.DeleteTexture(Handle);
    }
}

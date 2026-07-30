using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace Dungeon
{
    /// <summary>Immediate-mode 2D batch for text and filled quads (screen space, y-down).</summary>
    public sealed class Hud
    {
        readonly Font _font;
        readonly Shader _shader;
        readonly int _vao, _vbo;
        readonly List<float> _buf = new(4096);
        Matrix4 _proj;

        const string VS = @"#version 330 core
layout(location=0) in vec2 aPos;
layout(location=1) in vec2 aUV;
layout(location=2) in vec4 aColor;
uniform mat4 uProj;
out vec2 vUV; out vec4 vColor;
void main(){ vUV = aUV; vColor = aColor; gl_Position = vec4(aPos,0.0,1.0) * uProj; }";

        const string FS = @"#version 330 core
in vec2 vUV; in vec4 vColor;
uniform sampler2D uTex;
out vec4 frag;
void main(){ float a = texture(uTex, vUV).a; frag = vec4(vColor.rgb, vColor.a * a); }";

        public Hud(Font font)
        {
            _font = font;
            _shader = new Shader(VS, FS);
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            int stride = 8 * sizeof(float);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
            GL.BindVertexArray(0);
        }

        public void Begin(int screenW, int screenH)
        {
            _buf.Clear();
            _proj = Matrix4.CreateOrthographicOffCenter(0, screenW, screenH, 0, -1, 1);
        }

        void Push(float x, float y, float u, float v, Vector4 c)
        {
            _buf.Add(x); _buf.Add(y); _buf.Add(u); _buf.Add(v);
            _buf.Add(c.X); _buf.Add(c.Y); _buf.Add(c.Z); _buf.Add(c.W);
        }

        void Rect(float x, float y, float w, float h, float u0, float v0, float u1, float v1, Vector4 c)
        {
            Push(x, y, u0, v0, c); Push(x + w, y, u1, v0, c); Push(x + w, y + h, u1, v1, c);
            Push(x, y, u0, v0, c); Push(x + w, y + h, u1, v1, c); Push(x, y + h, u0, v1, c);
        }

        public void Quad(float x, float y, float w, float h, Vector4 color)
        {
            _font.SolidUV(out float u, out float v);
            Rect(x, y, w, h, u, v, u, v, color);
        }

        public float TextWidth(string s, float scale) => s.Length * 7 * scale;

        public void Text(string s, float x, float y, float scale, Vector4 color)
        {
            float cx = x;
            foreach (char raw in s)
            {
                char c = raw;
                if (c >= 'a' && c <= 'z') c = (char)(c - 32); // font is uppercase-only
                if (c != ' ')
                {
                    _font.CellUV(c, out float u0, out float v0, out float u1, out float v1);
                    Rect(cx, y, 8 * scale, 8 * scale, u0, v0, u1, v1, color);
                }
                cx += 7 * scale;
            }
        }

        /// <summary>Centered text convenience.</summary>
        public void TextCentered(string s, float cx, float y, float scale, Vector4 color)
            => Text(s, cx - TextWidth(s, scale) / 2f, y, scale, color);

        public void End()
        {
            if (_buf.Count == 0) return;
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _shader.Use();
            _shader.Set("uProj", _proj);
            _shader.Set("uTex", 0);
            _font.Atlas.Bind(TextureUnit.Texture0);

            var arr = _buf.ToArray();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, arr.Length * sizeof(float), arr, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, arr.Length / 8);
            GL.Enable(EnableCap.DepthTest);
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using Sledge.Common;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.DataStructures.Models;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Materials;
using Sledge.Rendering.OpenGL.Arrays;
using Sledge.Rendering.OpenGL.Shaders;
using Sledge.Rendering.Scenes;
using Sledge.Rendering.Scenes.Elements;

namespace Sledge.Rendering.OpenGL
{
    public class OpenGLRenderer : IRenderer
    {
        private readonly Dictionary<IViewport, ViewportData> _viewportData;
        private readonly Dictionary<Scene, SceneData> _sceneData;
        private readonly TextureStorage _textureStorage;
        private readonly MaterialStorage _materialStorage;
        private readonly ModelStorage _modelStorage;
        private Scene _activeScene;
        private bool _initialised;

        private Passthrough _shaderProgram;
        private ModelShader _modelShaderProgram;

        public ITextureStorage Textures { get { return _textureStorage; } }
        public IMaterialStorage Materials { get { return _materialStorage; } }
        public IModelStorage Models { get { return _modelStorage; } }
        public StringTextureManager StringTextureManager { get; private set; }
        public List<ITextureProvider> TextureProviders { get; private set; }

        private readonly ConcurrentQueue<string> _requestedTextureQueue;

        public Matrix4 SelectionTransform { get; set; }

        public OpenGLRenderer()
        {
            _viewportData = new Dictionary<IViewport, ViewportData>();
            _sceneData = new Dictionary<Scene, SceneData>();
            _textureStorage = new TextureStorage();
            _materialStorage = new MaterialStorage(this);
            _modelStorage = new ModelStorage();
            _initialised = false;

            SelectionTransform = Matrix4.Identity;
            StringTextureManager = new StringTextureManager(this);
            TextureProviders = new List<ITextureProvider>();
            _requestedTextureQueue = new ConcurrentQueue<string>();
        }

        public void RequestTexture(string name)
        {
            _requestedTextureQueue.Enqueue(name);
        }

        private void InitialiseRenderer()
        {
            if (_initialised) return;

            _shaderProgram = new Passthrough();
            _modelShaderProgram = new ModelShader();

            _materialStorage.Initialise();
            _textureStorage.Initialise();

            _initialised = true;
        }

        public IViewport CreateViewport()
        {
            var view = new OpenGLViewport();
            view.Render += RenderViewport;
            view.Update += UpdateViewport;
            return view;
        }

        public void DestroyViewport(IViewport viewport)
        {
            var data = _viewportData.ContainsKey(viewport) ? _viewportData[viewport] : null;
            if (data != null)
            {
                data.Framebuffer.Dispose();
                _viewportData.Remove(viewport);
            }

            var view = viewport as OpenGLViewport;
            if (view == null) return;

            view.Render -= RenderViewport;
            view.Update -= UpdateViewport;
            view.Dispose();
        }

        public Scene CreateScene()
        {
            var scene = new Scene { TrackChanges = true };
            _sceneData.Add(scene, null);
            return scene;
        }

        public void SetActiveScene(Scene scene)
        {
            if (_sceneData.ContainsKey(scene)) _activeScene = scene;
        }

        public void RemoveScene(Scene scene)
        {
            if (_activeScene == scene) _activeScene = null;
            if (_sceneData.ContainsKey(scene))
            {
                var data = _sceneData[scene];
                if (data != null) data.Dispose();
            }
            _sceneData.Remove(scene);
        }

        private void InitialiseViewport(IViewport viewport, ViewportData data)
        {
            if (!data.Initialised)
            {
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Lequal);

                GL.Enable(EnableCap.CullFace);
                GL.CullFace(CullFaceMode.Front);

                GL.Enable(EnableCap.Texture2D);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

                GL.PointSize(4);

                data.Initialised = true;
            }
            if (data.Width != viewport.Control.Width || data.Height != viewport.Control.Height)
            {
                data.Width = viewport.Control.Width;
                data.Height = viewport.Control.Height;
                data.Framebuffer.Size = new Size(viewport.Control.Width, viewport.Control.Height);
            }
        }

        private void UpdateViewport(IViewport viewport, Frame frame)
        {
            Materials.Update(frame);
            Models.Update(frame);
            StringTextureManager.Update(frame);
        }

        private void ProcessTextureQueue()
        {
            var names = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                if (_requestedTextureQueue.IsEmpty) break;

                string name;
                if (_requestedTextureQueue.TryDequeue(out name))
                {
                    names.Add(name);
                }
            }
            foreach (var tp in TextureProviders)
            {
                var list = names.Where(x => tp.Exists(x)).ToList();
                names.RemoveAll(list.Contains);
                foreach (var td in tp.Fetch(list))
                {
                    Textures.Create(td.Name, td.Bitmap, td.Width, td.Height, td.Flags);
                }
            }
        }

        private void RenderViewport(IViewport viewport, Frame frame)
        {
            if (_activeScene == null) return;

            var vpData = GetViewportData(viewport);
            var scData = GetSceneData(_activeScene);

            InitialiseRenderer();
            InitialiseViewport(viewport, vpData);
            scData.Array.ApplyChanges();
            vpData.ElementArray.Update(scData.Array.Elements);
            ProcessTextureQueue();
            
            // Set up FBO
            vpData.Framebuffer.Bind();
            
            GL.ClearColor(Color.Black);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            scData.Array.Render(this, _shaderProgram, _modelShaderProgram, viewport);
            vpData.ElementArray.Render(this, _shaderProgram, viewport);

            // Blit FBO
            vpData.Framebuffer.Unbind();
            vpData.Framebuffer.Render();

        }

        private ViewportData GetViewportData(IViewport viewport)
        {
            if (!_viewportData.ContainsKey(viewport))
            {
                var data = new ViewportData(this, viewport);
                _viewportData.Add(viewport, data);
            }
            return _viewportData[viewport];
        }

        private SceneData GetSceneData(Scene scene)
        {
            if (!_sceneData.ContainsKey(scene) || _sceneData[scene] == null)
            {
                var data = new SceneData(this, scene);
                _sceneData[scene] = data;
            }
            return _sceneData[scene];
        }

        private class ViewportData : IDisposable
        {
            public Framebuffer Framebuffer { get; private set; }
            public ElementVertexArray ElementArray { get; private set; }
            public bool Initialised { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }

            public ViewportData(IRenderer renderer, IViewport viewport)
            {
                Width = viewport.Control.Width;
                Height = viewport.Control.Height;
                Framebuffer = new Framebuffer(Width, Height);
                ElementArray = new ElementVertexArray(renderer, viewport, new Element[0]);
                Initialised = false;
            }

            public void Dispose()
            {
                Framebuffer.Dispose();
                ElementArray.Dispose();
            }
        }

        private class SceneData : IDisposable
        {
            public Scene Scene { get; set; }
            public OctreeVertexArray Array { get; private set; }

            public SceneData(OpenGLRenderer renderer, Scene scene)
            {
                Scene = scene;
                Array = new OctreeVertexArray(renderer, scene, Math.Min(-float.MinValue, float.MaxValue));
            }

            public void Dispose()
            {
                Array.Dispose();
            }
        }
    }
}
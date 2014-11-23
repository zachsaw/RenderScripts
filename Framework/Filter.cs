using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using SharpDX;
using Mpdn.RenderScript.Scaler;
using TransformFunc = System.Func<System.Drawing.Size, System.Drawing.Size>;

namespace Mpdn.RenderScript
{
    public interface ITextureCache
    {
        ITexture GetTexture(Size textureSize);
        void PutTexture(ITexture texture);
        void PutTempTexture(ITexture texture);
    }

    public interface IFilter
    {
        IFilter[] InputFilters { get; }
        ITexture OutputTexture { get; }
        Size OutputSize { get; }
        int FilterIndex { get; }
        int LastDependentIndex { get; }
        void NewFrame();
        void Render(ITextureCache Cache);
        void ReleaseTexture(ITextureCache Cache);
        void Initialize(int time = 1);
    }

    public interface IScaleableFilter : IFilter
    {
        Size PrescaleSize { set; }
    }

    public interface IConvertableFilter : IFilter
    {
        bool WantYuv { get; set; }
    }

    public class TextureCache : ITextureCache
    {
        private List<ITexture> SavedTextures = new List<ITexture>();
        private List<ITexture> OldTextures = new List<ITexture>();
        private List<ITexture> TempTextures = new List<ITexture>();

        public ITexture GetTexture(Size textureSize)
        {
            foreach (var list in new[] { SavedTextures, OldTextures })
            {
                var index = list.FindIndex(x => (x.Width == textureSize.Width) && (x.Height == textureSize.Height));
                if (index < 0) continue;

                var texture = list[index];
                list.RemoveAt(index);
                return texture;
            }

            return Renderer.CreateRenderTarget(textureSize);
        }

        public void PutTempTexture(ITexture texture)
        {
            TempTextures.Add(texture);
            SavedTextures.Add(texture);
        }

        public void PutTexture(ITexture texture)
        {
            SavedTextures.Add(texture);
        }

        public void FlushTextures()
        {
            foreach (var texture in OldTextures)
                Common.Dispose(texture);

            foreach (var texture in TempTextures)
                SavedTextures.Remove(texture);

            OldTextures = SavedTextures;
            TempTextures = new List<ITexture>();
            SavedTextures = new List<ITexture>();
        }

        public void Dispose()
        {
            FlushTextures();
            FlushTextures();
        }
    }

    public abstract class Filter : IFilter
    {
        protected Filter(params IFilter[] inputFilters)
        {
            if (inputFilters == null || inputFilters.Length == 0 || inputFilters.Any(f => f == null))
            {
                throw new ArgumentNullException("inputFilters");
            }

            Initialized = false;
            InputFilters = inputFilters;
        }

        public abstract void Render(IEnumerable<ITexture> inputs);

        #region IFilter Implementation

        protected bool Updated { get; set; }
        protected bool Initialized { get; set; }
        protected virtual int Passthrough { get { return -1; } }

        public IFilter[] InputFilters { get; private set; }
        public ITexture OutputTexture { get; private set; }

        public abstract Size OutputSize { get; }

        public int FilterIndex { get; private set; }
        public int LastDependentIndex { get; private set; }

        public virtual void Initialize(int time = 1)
        {
            LastDependentIndex = time;

            if (Initialized)
                return;

            foreach (var filter in InputFilters)
            {
                filter.Initialize(LastDependentIndex);
                LastDependentIndex = filter.LastDependentIndex;
            }

            FilterIndex = LastDependentIndex;

            foreach (var filter in InputFilters)
            {
                filter.Initialize(FilterIndex);
            }

            LastDependentIndex++;

            Initialized = true;
        }

        public virtual void NewFrame()
        {
            if (InputFilters == null)
                return;

            if (!Updated)
                return;

            Updated = false;

            foreach (var filter in InputFilters)
            {
                filter.NewFrame();
            }
        }

        public virtual void Render(ITextureCache Cache)
        {
            if (Updated)
                return;

            Updated = true;

            foreach (var filter in InputFilters)
                filter.Render(Cache);

            var inputTextures = InputFilters.Select(f => f.OutputTexture);

            if (Passthrough < 0)
            {
                OutputTexture = Cache.GetTexture(OutputSize);

                Render(inputTextures);

                foreach (var filter in InputFilters)
                {
                    if (filter.LastDependentIndex <= FilterIndex)
                        filter.ReleaseTexture(Cache);
                }
            }
            else
            {
                OutputTexture = inputTextures.ElementAt(Passthrough);

                foreach (var filter in InputFilters)
                {
                    if (filter.OutputTexture != OutputTexture
                    &&  filter.LastDependentIndex <= FilterIndex)
                        filter.ReleaseTexture(Cache);
                }
            }
        }

        public virtual void ReleaseTexture(ITextureCache Cache) {
            Cache.PutTexture(OutputTexture);
            OutputTexture = null;
        }

        #endregion
    }

    public abstract class BaseSourceFilter : IFilter
    {
        protected BaseSourceFilter(params IFilter[] inputFilters)
        {
            InputFilters = inputFilters;
        }

        #region IFilter Implementation

        public IFilter[] InputFilters { get; protected set; }
        public abstract ITexture OutputTexture { get; }

        public abstract Size OutputSize { get; }

        public virtual int FilterIndex
        {
            get { return 0; }
        }

        public virtual int LastDependentIndex { get; private set; }

        public void Dispose()
        {
        }

        public void Initialize(int time = 1)
        {
            LastDependentIndex = time;
        }

        public void NewFrame()
        {
        }

        public void Render(ITextureCache Cache)
        {
        }

        public virtual void ReleaseTexture(ITextureCache Cache)
        {
            Cache.PutTempTexture(OutputTexture);
        }

        #endregion
    }

    public sealed class SourceFilter : BaseSourceFilter, IScaleableFilter, IConvertableFilter
    {
        public Size PrescaleSize { get; set; }
        public bool WantYuv      { get; set; }

        #region IFilter Implementation

        public override ITexture OutputTexture
        {
            get { return Renderer.InputRenderTarget; }
        }

        public override Size OutputSize
        {
            get { return PrescaleSize; }
        }

        #endregion
    }

    public class YSourceFilter : BaseSourceFilter
    {
        public override ITexture OutputTexture
        {
            get { return Renderer.TextureY; }
        }

        public override Size OutputSize
        {
            get { return Renderer.LumaSize; }
        }

        public override void ReleaseTexture(ITextureCache Cache)
        {
        }
    }

    public class USourceFilter : BaseSourceFilter
    {
        public override ITexture OutputTexture
        {
            get { return Renderer.TextureU; }
        }

        public override Size OutputSize
        {
            get { return Renderer.ChromaSize; }
        }

        public override void ReleaseTexture(ITextureCache Cache)
        {
        }
    }

    public class VSourceFilter : BaseSourceFilter
    {
        public override ITexture OutputTexture
        {
            get { return Renderer.TextureV; }
        }

        public override Size OutputSize
        {
            get { return Renderer.ChromaSize; }
        }

        public override void ReleaseTexture(ITextureCache Cache)
        {
        }
    }

    public class RgbFilter : Filter, IConvertableFilter
    {
        public bool WantYuv { get; set; }
        protected override int Passthrough
        {
            get { return WantYuv ? 0 : -1; }
        }

        public RgbFilter(IFilter inputFilter)
            : base(inputFilter)
        {
            if (inputFilter is IConvertableFilter)
            {
                (inputFilter as IConvertableFilter).WantYuv = false;
                WantYuv = true; // Skips conversion
            }
        }

        public override Size OutputSize
        {
            get { return InputFilters[0].OutputSize; }
        }

        public override void Render(IEnumerable<ITexture> inputs)
        {
            Renderer.ConvertToRgb(OutputTexture, inputs.Single(), Renderer.Colorimetric);
        }
    }

    public class YuvFilter : Filter, IConvertableFilter
    {
        public bool WantYuv { get; set; }
        protected override int Passthrough
        {
            get { return WantYuv ? -1 : 0; }
        }

        public YuvFilter(IFilter inputFilter)
            : base(inputFilter)
        {
            if (inputFilter is IConvertableFilter)
            {
                (inputFilter as IConvertableFilter).WantYuv = true;
                WantYuv = false; // Skips conversion
            }
        }

        public override Size OutputSize
        {
            get { return InputFilters[0].OutputSize; }
        }

        public override void Render(IEnumerable<ITexture> inputs)
        {
            Renderer.ConvertToYuv(OutputTexture, inputs.Single(), Renderer.Colorimetric);
        }
    }

    public class ResizeFilter : Filter
    {
        private readonly Size m_OutputSize;
        private readonly IScaler m_Upscaler;
        private readonly IScaler m_Downscaler;

        protected override int Passthrough
        {
            get { return OutputSize == InputFilters[0].OutputSize ? 0 : -1; }
        }

        public ResizeFilter(IFilter inputFilter, Size outputSize,
            IScaler upscaler, IScaler downscaler)
            : base(inputFilter)
        {
            if (InputFilters[0] is IScaleableFilter)
                (InputFilters[0] as IScaleableFilter).PrescaleSize = outputSize;

            m_OutputSize = outputSize;
            m_Upscaler = upscaler;
            m_Downscaler = downscaler;
        }

        public override Size OutputSize
        {
            get { return m_OutputSize; }
        }

        public override void Render(IEnumerable<ITexture> inputs)
        {
            Renderer.Scale(OutputTexture, inputs.Single(), m_Upscaler, m_Downscaler);
        }
    }

    public class ShaderFilter : Filter
    {
        public ShaderFilter(IShader shader, int sizeIndex, bool linearSampling,
            params IFilter[] inputFilters)
            : this(shader, s => new Size(s.Width, s.Height), sizeIndex, linearSampling, inputFilters)
        {
        }

        public ShaderFilter(IShader shader, TransformFunc transform, int sizeIndex,
            bool linearSampling, params IFilter[] inputFilters)
            : base(inputFilters)
        {
            if (sizeIndex < 0 || sizeIndex >= inputFilters.Length || inputFilters[sizeIndex] == null)
            {
                throw new IndexOutOfRangeException(String.Format("No valid input filter at index {0}", sizeIndex));
            }

            Shader = shader;
            LinearSampling = linearSampling;
            Transform = transform;
            SizeIndex = sizeIndex;
        }

        protected IShader Shader { get; private set; }
        protected bool LinearSampling { get; private set; }
        protected int Counter { get; private set; }
        protected TransformFunc Transform { get; private set; }
        protected int SizeIndex { get; private set; }

        public override Size OutputSize
        {
            get { return Transform(InputFilters[SizeIndex].OutputSize); }
        }

        public override void Render(IEnumerable<ITexture> inputs)
        {
            LoadInputs(inputs);
            Renderer.Render(OutputTexture, Shader);
        }

        protected virtual void LoadInputs(IEnumerable<ITexture> inputs)
        {
            var i = 0;
            foreach (var input in inputs)
            {
                Shader.SetTextureConstant(i, input.Texture, LinearSampling, false);
                Shader.SetConstant(String.Format("size{0}", i),
                    new Vector4(input.Width, input.Height, 1.0f/input.Width, 1.0f/input.Height), false);
                i++;
            }

            // Legacy constants 
            var output = OutputTexture;
            Shader.SetConstant(0, new Vector4(output.Width, output.Height, Counter++, Stopwatch.GetTimestamp()),
                false);
            Shader.SetConstant(1, new Vector4(1.0f/output.Width, 1.0f/output.Height, 0, 0), false);
        }
    }
}
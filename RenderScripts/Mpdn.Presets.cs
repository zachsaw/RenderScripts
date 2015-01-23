using System;
using System.Windows.Forms;
using Mpdn.PlayerExtensions.GitHub;

namespace Mpdn.RenderScript
{
    namespace Mpdn.ScriptChain
    {
        public abstract class PresetRenderScript : IRenderScriptUi
        {
            protected abstract RenderScriptPreset Preset { get; }

            protected virtual IRenderScriptUi Script { get { return Preset.Script ?? new ScriptChainScript(); } }

            public virtual IRenderScript CreateRenderScript()
            {
                return Script.CreateRenderScript();
            }

            public virtual void Destroy()
            {
                Script.Destroy();
            }

            public virtual void Initialize() { }

            public virtual bool ShowConfigDialog(IWin32Window owner)
            {
                var s = Script as ScriptChainScript;
                if (s != null && s.Chain == null)
                {
                    MessageBox.Show(owner, "No presets");
                    return false;
                }

                return Script.ShowConfigDialog(owner);
            }

            public virtual ScriptDescriptor Descriptor
            {
                get 
                {
                    var descriptor = Script.Descriptor;
                    descriptor.Guid = Preset.Guid;
                    return descriptor;
                }
            }
        }

        public class ActivePresetRenderScript : PresetRenderScript
        {
            private readonly Guid m_Guid = new Guid("B1F3B882-3E8F-4A8C-B225-30C9ABD67DB1");

            protected override RenderScriptPreset Preset 
            {
                get { return PresetExtension.ActivePreset ?? new RenderScriptPreset { Script = Script }; } 
            }

            public override void Initialize()
            {
                base.Initialize();

                PresetExtension.ScriptGuid = m_Guid;
            }

            public override ScriptDescriptor Descriptor
            {
                get
                {
                    var descriptor = base.Descriptor;
                    descriptor.Name = "Preset";
                    descriptor.Guid = m_Guid;
                    descriptor.Description = "Active Preset";
                    return descriptor;
                }
            }
        }
    }
}
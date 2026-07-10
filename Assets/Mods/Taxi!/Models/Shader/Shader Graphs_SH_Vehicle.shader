Shader "Shader Graphs/SH_Vehicle" {
	Properties {
		Color_3d0f0cdbe6b74be28a1a5be5bab71dea ("Tint", Vector) = (1,0,0,0)
		Color_f78fac473bac467092fb27521e9f71ea ("FresnelColor", Vector) = (0.8396226,0.4475347,0.4475347,0)
		Vector1_481fa2a8a5e94165a039319bfd512b76 ("FresnelPower", Float) = 1
		[NoScaleOffset] Texture2D_3d0babbacbb04731a784ae3943688e65 ("Diffuse+mask", 2D) = "white" {}
		[NoScaleOffset] Texture2D_9b32f246716c41009146a43593b55e3f ("Normal", 2D) = "white" {}
		[NoScaleOffset] Texture2D_196dcf5fb385498aa0919d56d7cd684d ("Met+AO+Smooth", 2D) = "white" {}
		Vector1_41991b689ccf4932ad504975ac34399a ("SmoothnessBoost", Range(0.5, 1.5)) = 1
		_Min ("Min", Range(0, 1)) = 0
		_Max ("Max", Range(0, 0.097)) = 0.097
		AOmin ("AOmin", Range(0, 1)) = 0
		AOmax ("AOmax", Range(0, 1)) = 1
		[NoScaleOffset] _EmissionTexture ("EmissionTexture", 2D) = "black" {}
		[NoScaleOffset] _EmissionMask ("EmissionMask", 2D) = "white" {}
		_FrontLightStrength ("FrontLightStrength (R)", Float) = 0
		_BackLightStrength ("BackLightStrength (G)", Float) = 0
		_ReverseLightStrength ("ReverseLightStrength (B)", Float) = 0
		[NoScaleOffset] _IndicatorsMask ("IndicatorsMask", 2D) = "black" {}
		[ToggleUI] _IsBlinkerOn ("IsBlinkerOn", Float) = 0
		[ToggleUI] _IsRightBlinkerOn ("IsRightBlinkerOn", Float) = 0
		_RightBlinkerStrength ("RightBlinkerStrength (R)", Float) = 0
		[ToggleUI] _IsLeftBlinkerOn ("IsLeftBlinkerOn", Float) = 0
		_LeftBlinkerStrength ("LeftBlinkerStrength (G)", Float) = 0
		_BlinkerOffset ("BlinkerOffset", Float) = 5
		_BlinkerSpeed ("BlinkerSpeed", Float) = 1
		_Dirtiness ("Dirtiness", Range(0, 1)) = 0
		[NoScaleOffset] _DirtDiffuse ("DirtDiffuse", 2D) = "black" {}
		[NoScaleOffset] [Normal] _DirtNormal ("DirtNormal", 2D) = "bump" {}
		[NoScaleOffset] _Dirt_Met_AO_Smooth ("Dirt Met+AO+Smooth", 2D) = "white" {}
		[HideInInspector] _EmissionColor ("Color", Vector) = (1,1,1,1)
		[HideInInspector] _RenderQueueType ("Float", Float) = 1
		[ToggleUI] [HideInInspector] _AddPrecomputedVelocity ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _DepthOffsetEnable ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _ConservativeDepthOffsetEnable ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _TransparentWritingMotionVec ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _AlphaCutoffEnable ("Boolean", Float) = 0
		[HideInInspector] _TransparentSortPriority ("_TransparentSortPriority", Float) = 0
		[ToggleUI] [HideInInspector] _UseShadowThreshold ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _DoubleSidedEnable ("Boolean", Float) = 0
		[Enum(Flip, 0, Mirror, 1, None, 2)] [HideInInspector] _DoubleSidedNormalMode ("Float", Float) = 2
		[HideInInspector] _DoubleSidedConstants ("Vector4", Vector) = (1,1,-1,0)
		[Enum(Auto, 0, On, 1, Off, 2)] [HideInInspector] _DoubleSidedGIMode ("Float", Float) = 0
		[ToggleUI] [HideInInspector] _TransparentDepthPrepassEnable ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _TransparentDepthPostpassEnable ("Boolean", Float) = 0
		[HideInInspector] _SurfaceType ("Float", Float) = 0
		[HideInInspector] _BlendMode ("Float", Float) = 0
		[HideInInspector] _SrcBlend ("Float", Float) = 1
		[HideInInspector] _DstBlend ("Float", Float) = 0
		[HideInInspector] _AlphaSrcBlend ("Float", Float) = 1
		[HideInInspector] _AlphaDstBlend ("Float", Float) = 0
		[ToggleUI] [HideInInspector] _ZWrite ("Boolean", Float) = 1
		[ToggleUI] [HideInInspector] _TransparentZWrite ("Boolean", Float) = 0
		[HideInInspector] _CullMode ("Float", Float) = 2
		[ToggleUI] [HideInInspector] _EnableFogOnTransparent ("Boolean", Float) = 1
		[HideInInspector] _CullModeForward ("Float", Float) = 2
		[Enum(Front, 1, Back, 2)] [HideInInspector] _TransparentCullMode ("Float", Float) = 2
		[Enum(UnityEditor.Rendering.HighDefinition.OpaqueCullMode)] [HideInInspector] _OpaqueCullMode ("Float", Float) = 2
		[HideInInspector] _ZTestDepthEqualForOpaque ("Float", Float) = 3
		[Enum(UnityEngine.Rendering.CompareFunction)] [HideInInspector] _ZTestTransparent ("Float", Float) = 4
		[ToggleUI] [HideInInspector] _TransparentBackfaceEnable ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _RequireSplitLighting ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _ReceivesSSR ("Boolean", Float) = 1
		[ToggleUI] [HideInInspector] _ReceivesSSRTransparent ("Boolean", Float) = 0
		[ToggleUI] [HideInInspector] _EnableBlendModePreserveSpecularLighting ("Boolean", Float) = 1
		[ToggleUI] [HideInInspector] _SupportDecals ("Boolean", Float) = 1
		[ToggleUI] [HideInInspector] _ExcludeFromTUAndAA ("Boolean", Float) = 0
		[HideInInspector] _StencilRef ("Float", Float) = 0
		[HideInInspector] _StencilWriteMask ("Float", Float) = 6
		[HideInInspector] _StencilRefDepth ("Float", Float) = 8
		[HideInInspector] _StencilWriteMaskDepth ("Float", Float) = 9
		[HideInInspector] _StencilRefMV ("Float", Float) = 40
		[HideInInspector] _StencilWriteMaskMV ("Float", Float) = 41
		[HideInInspector] _StencilRefDistortionVec ("Float", Float) = 4
		[HideInInspector] _StencilWriteMaskDistortionVec ("Float", Float) = 4
		[HideInInspector] _StencilWriteMaskGBuffer ("Float", Float) = 15
		[HideInInspector] _StencilRefGBuffer ("Float", Float) = 10
		[HideInInspector] _ZTestGBuffer ("Float", Float) = 4
		[ToggleUI] [HideInInspector] _RayTracing ("Boolean", Float) = 0
		[Enum(None, 0, Planar, 1, Sphere, 2, Thin, 3)] [HideInInspector] _RefractionModel ("Float", Float) = 0
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType" = "Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
			};

			struct Vertex_Stage_Output
			{
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			float4 frag(Vertex_Stage_Output input) : SV_TARGET
			{
				return float4(1.0, 1.0, 1.0, 1.0); // RGBA
			}

			ENDHLSL
		}
	}
	Fallback "Hidden/Shader Graph/FallbackError"
	//CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
}
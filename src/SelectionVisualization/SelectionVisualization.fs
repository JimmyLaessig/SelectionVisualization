namespace SelectionVisualization

open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Rendering.NanoVg
open Aardvark.SceneGraph
open Aardvark.Base.Rendering

open System
open System.Linq


open Shaders
open Lasso

module SelectionVisualization =
    

    type SelectionVisualizationParameters = 
        {
            viewTrafo               : IMod<Trafo3d>
            projTrafo               : IMod<Trafo3d>
            lasso                   : MouseKeyboard.VgmLasso
            selectionColor          : IMod<C4f>
            maxSelectionDistance    : IMod<float>
            volumeColor             : IMod<C4f>
            showVolumes             : IMod<bool>
            geometryPass            : RenderPass
            runtime                 : IRuntime
            framebufferSignature    : IFramebufferSignature

        }

    type Selection =
    | Single
    | And
    | Or
    | Xor
    | Subtract
    

    let private writeStencilBuffer  = Set.singleton DefaultSemantic.Stencil
    let private writeColorBuffer    = Set.ofList[DefaultSemantic.Colors; DefaultSemantic.Stencil]


    // Creates a list of renderpasses staring by the given renderpass
    // the first element in the list is at the same time the first renderPass to be executed
    let private appendRenderPass (renderPass : RenderPass) (numRenderPasses) = 


        let rec appendRenderPassRec (renderPassesBefore: list<RenderPass>)(counter : int) = 
            
             if counter = 0 then
                renderPassesBefore
             else
                match renderPassesBefore with
                | [] -> failwith ""
                | last::_ ->
                    let name    = sprintf "%i" (numRenderPasses - counter)
                    let newLast = last |> Rendering.RenderPass.after name RenderPassOrder.Arbitrary           
                    appendRenderPassRec (newLast :: renderPassesBefore) (counter - 1)    
                      
                       
        (appendRenderPassRec [renderPass] numRenderPasses) |> List.rev



    /// <summary>
    /// Initializes the Volume Selection technique. It receives a sceneGraph with a scene appended and the last used renderpass of the geometry and performs a selection on the scene. 
    /// The function uses IMods for all parameters that can change over time to adapt its behaviour accordingly. 
    /// The function returns the modified scenegraph with the selection volumes attached and its' last used renderpass.  
    /// <param name="sceneGraph"> The scenegraph containing the geometry</param>
    /// <param name="view">The cameras view transform</param>
    /// <param name="proj">The cameras projection transform</param>
    /// <param name="lasso">The lasso to perform selection with</param>
    /// <param name="selectionColor">The color of the selected pixel</param>
    /// <param name="selectionDistance"> The distance of a selection</param>
    /// <param name="volumeColor">The color of the volume, if rendered</param>
    /// <param name="showVolumes">Indicates if the volumes should be rendered visible</param>
    /// <param name="geometryPass">The last renderpass with with the geometry is rendered</param>
    /// <param name="runtime"></param>
    /// <param name="framebufferSignature"></param>
    /// </summary>
    let Init (p : SelectionVisualizationParameters) : (ISg * RenderPass) = 
       
      

        let invVolumeColor = p.volumeColor |> Mod.map (fun c-> (C4f(1.0f - c.R, 1.0f - c.G, 1.0f - c.B, c.A)))

        // Initialize Shading Effects
        let volumeAdditiveEffect    = p.runtime.PrepareEffect(p.framebufferSignature, [Shaders.trafo |> toEffect;  Shaders.ExtrudeCaps |> toEffect; DefaultSurfaces.uniformColor p.volumeColor  |> toEffect]) :> ISurface
        let volumeSubtractiveEffect = p.runtime.PrepareEffect(p.framebufferSignature, [Shaders.trafo |> toEffect;  Shaders.ExtrudeCaps |> toEffect; DefaultSurfaces.uniformColor invVolumeColor  |> toEffect]) :> ISurface
                          
        let normalizeStencilEffect  = p.runtime.PrepareEffect(p.framebufferSignature, [DefaultSurfaces.constantColor C4f.Black          |> toEffect]) :> ISurface         
        let hightlightEffect        = p.runtime.PrepareEffect(p.framebufferSignature, [DefaultSurfaces.uniformColor p.selectionColor    |> toEffect]) :> ISurface               
        
        
        // Full Screen Quad Geometry
//        let fullscreenQuad = IndexedGeometry (
//                                Mode = IndexedGeometryMode.TriangleList, IndexArray = [| 0; 1; 2; 0; 2; 3 |],
//                                       IndexedAttributes = SymDict.ofList [DefaultSemantic.Positions, [| V3f(-1.0, -1.0, 1.0); V3f(1.0, -1.0, 1.0); V3f(1.0, 1.0, 1.0); V3f(-1.0, 1.0, 1.0) |] :> Array])                         
//        


        // ----------------------------------------------------------------------------------------------- //
        // Adds a Selection polygon
        // ----------------------------------------------------------------------------------------------- //
        let addSelectionPolygon(polygon : Lasso.NearPlanePolygon)(renderVolumes: bool)(renderPassVolume : RenderPass)(renderPassNormalize : RenderPass)(selection: Selection) =
           

                let worldTrafo  = (polygon.View * polygon.Proj).Inverse |> Mod.constant     // Transformation into world space 
                let lightPos    = polygon.View.Inverse.GetModelOrigin()                     // Position of the "light" camera


                // Triangulate Polygon     
//                    let gpcPoly = GPC.GpcPolygon(polygon.Polygon.Points.ToArray())                   
//                    // Create lightcap Vertex Array           
//                    let lightcapVertices = 
//                        gpcPoly.ComputeTriangulation() 
//                            |> Array.map ( fun t -> [| t.P2; t.P1; t.P0 |] ) 
//                            |> Array.concat 
//                            |> Array.toList   
                    
                let lightcapVertices    = polygon.TriangleList;                       
                let lightcapPositions   = lightcapVertices.ToArray()   |> Array.map (fun n -> V3f(2.0 * n.X - 1.0, 1.0 - 2.0 * n.Y, 0.1))          

                // Create Geometry for lightcap 
                let lightcapGeometry = IndexedGeometry(       
                                            Mode = IndexedGeometryMode.TriangleList,
                                            IndexedAttributes = SymDict.ofList [ DefaultSemantic.Positions, lightcapPositions :> Array]) 
      
                    // Get the correct stencil modes and effects
                let (stencilModeVolume, stencilModeNormalize, name, effect)= 
                    match selection with
                    |Selection.Single   -> (StencilModes.Additive, StencilModes.NormalizeAfterOR, "No Normalizaion" ,volumeAdditiveEffect)
                    |Selection.Or       -> (StencilModes.Additive, StencilModes.NormalizeAfterOR, "OR Normalizaion" ,volumeAdditiveEffect)
                    |Selection.And      -> (StencilModes.Additive, StencilModes.NormalizeAfterAND,"AND Normalizaion" ,volumeAdditiveEffect)
                    |Selection.Xor      -> (StencilModes.Additive, StencilModes.NormalizeAfterXOR,"XOR Normalizaion" ,volumeAdditiveEffect)
                    |Selection.Subtract -> (StencilModes.Subtractive, StencilModes.NormalizeAfterSUBTRACT,"SUBTRACT Normalizaion",  volumeSubtractiveEffect)
                
                let buffers = if (renderVolumes) then Set.ofList[DefaultSemantic.Colors; DefaultSemantic.Stencil]  else (Set.singleton DefaultSemantic.Stencil)
           
                // Create Scenegraph for light/darkcap
                let lightcapSG = 
                    lightcapGeometry |> Sg.ofIndexedGeometry
                                        |> Sg.trafo        worldTrafo 
                                        |> Sg.viewTrafo    p.viewTrafo             
                                        |> Sg.projTrafo    p.projTrafo      
                                        |> Sg.surface      (effect                 |> Mod.constant) 
                                        |> Sg.blendMode    (BlendMode.Blend        |> Mod.constant)     
                                        |> Sg.stencilMode  (stencilModeVolume      |> Mod.constant)
                                        |> Sg.uniform      "selectionDistance" p.maxSelectionDistance 
                                        |> Sg.uniform      "lightPos" (lightPos    |> Mod.constant)                                   
                                        |> Sg.pass         renderPassVolume    
                                        |> Sg.writeBuffers (Some buffers)


                // Add Normalization Pass only if not SINGLE Selection                                   
                let sceneGraphVolumeNormalized = 
                
                    match selection with
                    |Selection.Single -> lightcapSG                                    
                    | _               -> Sg.fullScreenQuad
                                                |> Sg.surface        (normalizeStencilEffect        |> Mod.constant)
                                                |> Sg.depthTest      (Rendering.DepthTestMode.None  |> Mod.constant)                     
                                                |> Sg.stencilMode    (stencilModeNormalize          |> Mod.constant)        
                                                |> Sg.pass           (renderPassNormalize                          )             
                                                |> Sg.writeBuffers   (Some (Set.singleton DefaultSemantic.Stencil) ) 
                                                |> Sg.andAlso lightcapSG
                                    
                                                                      
                // Return combined scenegraph             
                Sg.group'[sceneGraphVolumeNormalized]                                                           


     
        // ----------------------------------------------------------------------------------------------- //
        // Inverts the current selection by flipping the stencil buffer
        // ----------------------------------------------------------------------------------------------- //
        let invertSelection(renderPass1 : RenderPass)(renderPass2 : RenderPass) = 

                           
            // Normalize Pass 1: Increment all values in the stencilbuffer
            let normalizePass1SG = 
                    Sg.fullScreenQuad

                        |> Sg.surface        (normalizeStencilEffect                 |> Mod.constant)                      
                        |> Sg.stencilMode    (StencilModes.NormalizeAfterINVERTPass1 |> Mod.constant)        
                        |> Sg.pass           renderPass1     
                        
            // Normalize Pass2: Set all values greater 1 to 0               
            let normalizePass2SG = 
                    Sg.fullScreenQuad
                        |> Sg.surface        (normalizeStencilEffect         |> Mod.constant)
                        |> Sg.depthTest      (Rendering.DepthTestMode.None   |> Mod.constant)                     
                        |> Sg.stencilMode    (StencilModes.NormalizeAfterXOR |> Mod.constant)        
                        |> Sg.pass           renderPass2    
                           
            let normalizeStencilSG = Sg.group'[normalizePass1SG; normalizePass2SG]

            let normalizeStencilSG = Sg.WriteBuffersApplicator(Some (Set.singleton DefaultSemantic.Stencil), (normalizeStencilSG |> Mod.constant)) :> ISg

            // Return combined scenegraph     
            Sg.group'[normalizeStencilSG;  normalizeStencilSG]                                            
                      


        // ----------------------------------------------------------------------------------------------- //
        // Recoursive function for selection
        // ----------------------------------------------------------------------------------------------- //
        let rec addSelectionToSceneGraph (selection : Lasso.Selection)(renderVolumes: bool)(renderPassList) : (ISg * list<RenderPass>)=

                
            // Perform Recoursion
            let (sg1, remainingRenderPasses1) = 
                match selection with
                        |Lasso.Selection.Single polygon         -> (Sg.ofList [] , renderPassList)
                        |Lasso.NoSelection                      -> (Sg.ofList [] , renderPassList)
                        |Lasso.Or (selection, polygon)          -> addSelectionToSceneGraph selection renderVolumes renderPassList
                        |Lasso.And (selection, polygon)         -> addSelectionToSceneGraph selection renderVolumes renderPassList
                        |Lasso.Xor (selection, polygon)         -> addSelectionToSceneGraph selection renderVolumes renderPassList
                        |Lasso.Subtract (selection, polygon)    -> addSelectionToSceneGraph selection renderVolumes renderPassList
                        |Lasso.Invert (selection)               -> addSelectionToSceneGraph selection renderVolumes renderPassList
                     
                
            // Perform selection volume rendering  
            let (sg2, remainingRenderPasses2) = 
                match remainingRenderPasses1 with
                | renderPass1 :: renderPass2 :: renderPasses ->  
                    let volumeSg =                       
                        match selection with                       
                        |Lasso.Selection.Single polygon         -> addSelectionPolygon polygon renderVolumes renderPass1 renderPass2 Selection.Single
                        |Lasso.Or (selection, polygon)          -> addSelectionPolygon polygon renderVolumes renderPass1 renderPass2 Selection.Or 
                        |Lasso.And (selection, polygon)         -> addSelectionPolygon polygon renderVolumes renderPass1 renderPass2 Selection.And
                        |Lasso.Xor (selection, polygon)         -> addSelectionPolygon polygon renderVolumes renderPass1 renderPass2 Selection.Xor
                        |Lasso.Subtract (selection, polygon)    -> addSelectionPolygon polygon renderVolumes renderPass1 renderPass2 Selection.Subtract
                        |Lasso.Invert (selection)               -> invertSelection renderPass1 renderPass2                                             
                        |Lasso.NoSelection                      -> Sg.ofList []
                    (volumeSg, renderPasses)
                                 
                | []    -> failwith "Not enough renderpasses allocated"
                | _::_  -> failwith "Not enough renderpasses allocated"
            
            (Sg.group'[sg1; sg2], remainingRenderPasses2)                   
        


        let highlightSelectionPass = Rendering.RenderPass.after "Highlight_SelectionPass" Rendering.RenderPassOrder.Arbitrary p.geometryPass
            
        
        
        // TODO Dynamically resize renderPasses
        let numRenderPasses = 99
        let renderPassList = appendRenderPass (RenderPass.after "first" Rendering.RenderPassOrder.Arbitrary p.geometryPass) numRenderPasses
       // let renderPassList2 = appendRenderPass (renderPassList.ElementAt (0)) 9


        // Scenegraph with a fullscreenquad to highlight the selection    
        let highlightSg = 
                Sg.fullScreenQuad
                |> Sg.surface        (hightlightEffect                      |> Mod.constant)
                |> Sg.depthTest      (Rendering.DepthTestMode.None          |> Mod.constant)                     
                |> Sg.blendMode      (BlendMode.Blend                       |> Mod.constant) 
                |> Sg.stencilMode    (StencilModes.stencilModeHighLightOnes |> Mod.constant)        
                |> Sg.pass           highlightSelectionPass  

       

        // ----------------------------------------------------------------------------------------------- //
        // Adaptive Function to listen for change in Selection
        // ----------------------------------------------------------------------------------------------- // 
        let sceneGraph = 
            let sg = 
                adaptive {
                    Log.warn "ASDASDASDASDASDASDASDASDASDAS"
                    let! lasso = p.lasso.Selection.selection
                    let! renderVolumes = p.showVolumes
                
                    let (volumeSg, _) = addSelectionToSceneGraph lasso renderVolumes renderPassList 
                
                    return volumeSg
                }
            sg  |> Sg.dynamic
                |> Sg.andAlso highlightSg

      

        // Return RenderPass after selection
        (sceneGraph, highlightSelectionPass)

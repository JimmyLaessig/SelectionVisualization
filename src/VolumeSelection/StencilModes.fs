namespace VolumeSelection

open Aardvark.Base
open Aardvark.Base.Rendering


module StencilModes =

    // Stencil Mode for Addtivie Selection (AND, OR, XOR, SINGLE)
    let Additive = Rendering.StencilMode(
                                IsEnabled       = true,
                                CompareFront    = StencilFunction(Rendering.StencilCompareFunction.Always, 0x01, 0xFFu),
                                OperationFront  = StencilOperation(StencilOperationFunction.Keep, StencilOperationFunction.DecrementWrap, StencilOperationFunction.Keep),
                                CompareBack     = StencilFunction(Rendering.StencilCompareFunction.Always, 0x01, 0xFFu),
                                OperationBack   = StencilOperation(StencilOperationFunction.Keep, StencilOperationFunction.IncrementWrap, StencilOperationFunction.Keep)
                            ) 
                             
    // Stencil Mode for SUBTRACT Selection
    let Subtractive = Rendering.StencilMode(
                                IsEnabled       = true,
                                CompareFront    = StencilFunction(Rendering.StencilCompareFunction.Always, 0x01, 0xFFu),
                                OperationFront  = StencilOperation(StencilOperationFunction.Keep, StencilOperationFunction.IncrementWrap, StencilOperationFunction.Keep),
                                CompareBack     = StencilFunction(Rendering.StencilCompareFunction.Always, 0x01, 0xFFu),
                                OperationBack   = StencilOperation(StencilOperationFunction.Keep, StencilOperationFunction.DecrementWrap, StencilOperationFunction.Keep)
                            )  

    // Stencil Mode for highlighting the selection
    let stencilModeHighLightOnes = Rendering.StencilMode(
                                        IsEnabled   = true,
                                        Compare     = Rendering.StencilFunction(Rendering.StencilCompareFunction.Equal, 1, 0xFFu),
                                        Operation   = Rendering.StencilOperation(Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep)
                                   ) 
    // Stencil Mode for highlighting the selection
    let stencilModeHighLightZeros = Rendering.StencilMode(
                                        IsEnabled   = true,
                                        Compare     = Rendering.StencilFunction(Rendering.StencilCompareFunction.Equal, 0, 0xFFu),
                                        Operation   = Rendering.StencilOperation(Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep)
                                   ) 
    
                                 
    // Stencil Mode to normalize after AND Selection
    let NormalizeAfterAND = Rendering.StencilMode(
                                        IsEnabled   = true,
                                        Compare     = Rendering.StencilFunction(Rendering.StencilCompareFunction.Greater, 1, 0xFFu),
                                        Operation   = Rendering.StencilOperation(Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Decrement)                      
                                    )  
                                                                        
    // Stencil Mode to normalize after OR Selection
    let NormalizeAfterOR = Rendering.StencilMode(
                                        IsEnabled   = true,
                                        Compare     = Rendering.StencilFunction(Rendering.StencilCompareFunction.Greater, 1, 0xFFu),
                                        Operation   = Rendering.StencilOperation(Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Replace)                      
                                    )  
                                     
    // Stencil Mode to normalize after XOR Selection
    let NormalizeAfterXOR = Rendering.StencilMode(
                                        IsEnabled   = true,
                                        Compare     = Rendering.StencilFunction(Rendering.StencilCompareFunction.Greater, 2, 0xFFu),
                                        Operation   = Rendering.StencilOperation(Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Zero)                      
                                    )      
                                     
    // Stencil Mode to normalize after SUBTRACT Selection
    let NormalizeAfterSUBTRACT = Rendering.StencilMode(
                                        IsEnabled   = true,
                                        Compare     = Rendering.StencilFunction(Rendering.StencilCompareFunction.Equal, 1, 0xFFu),
                                        Operation   = Rendering.StencilOperation(Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.Zero)                      
                                    )

    // Stencil Mode to normalize after Invert - Pass1
    let NormalizeAfterINVERTPass1 = Rendering.StencilMode(
                                        IsEnabled   = true,
                                        Compare     = Rendering.StencilFunction(Rendering.StencilCompareFunction.Always, 0, 0xFFu),
                                        Operation   = Rendering.StencilOperation(Rendering.StencilOperationFunction.Keep, Rendering.StencilOperationFunction.IncrementWrap, Rendering.StencilOperationFunction.Keep)                                                         
                                    )                                                 
                                    
                                                                   
                                 

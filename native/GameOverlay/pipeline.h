// ImGui backs up only a D3D11.0 subset; VSSetConstantBuffers loses D3D11.1
// offsets/counts. A separate runtime state object isolates every pipeline stage,
// including game predicates and stream-output bindings.
struct IsolatedPipelineState {
    ID3D11DeviceContext1* ctx;
    ComPtr<ID3DDeviceContextState> gameState;
    IsolatedPipelineState(ID3D11DeviceContext1* context,ID3DDeviceContextState* overlayState):ctx(context) {
        ctx->SwapDeviceContextState(overlayState,&gameState);
    }
    ~IsolatedPipelineState() {
        // Do not retain backbuffers in the inactive overlay state across resize.
        ctx->ClearState();
        ctx->SwapDeviceContextState(gameState.Get(),nullptr);
    }
    IsolatedPipelineState(const IsolatedPipelineState&)=delete;
    IsolatedPipelineState& operator=(const IsolatedPipelineState&)=delete;
};
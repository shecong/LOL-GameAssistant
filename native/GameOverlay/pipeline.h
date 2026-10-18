// The upstream ImGui backend does not restore HS/DS/CS or output targets/UAVs.
// Restore these in addition to the backend's VS/PS/GS/IA/RS state preservation.
struct ExtraPipelineState {
    ID3D11DeviceContext* ctx;
    ID3D11RenderTargetView* targets[8]{};
    ComPtr<ID3D11DepthStencilView> depth;
    ComPtr<ID3D11HullShader> hull; ComPtr<ID3D11DomainShader> domain; ComPtr<ID3D11ComputeShader> compute;
    ID3D11ClassInstance* hi[256]{}, *di[256]{}, *ci[256]{};
    UINT hc=256,dc=256,cc=256;
    ID3D11UnorderedAccessView* uavs[8]{};
    explicit ExtraPipelineState(ID3D11DeviceContext* value):ctx(value) {
        ctx->OMGetRenderTargets(8,targets,&depth);
        ctx->OMGetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,0,8,uavs);
        ctx->HSGetShader(&hull,hi,&hc); ctx->DSGetShader(&domain,di,&dc); ctx->CSGetShader(&compute,ci,&cc);
    }
    ~ExtraPipelineState() {
        UINT count=0; for(UINT i=0;i<8;++i) if(targets[i]) count=i+1;
        UINT preserve[8]; std::fill_n(preserve,8,UINT(-1));
        ctx->OMSetRenderTargetsAndUnorderedAccessViews(count,targets,depth.Get(),count,8-count,uavs+count,preserve);
        ctx->HSSetShader(hull.Get(),hi,hc); ctx->DSSetShader(domain.Get(),di,dc); ctx->CSSetShader(compute.Get(),ci,cc);
        for(auto p:targets) if(p) p->Release(); for(auto p:uavs) if(p) p->Release();
        for(auto p:hi) if(p) p->Release(); for(auto p:di) if(p) p->Release(); for(auto p:ci) if(p) p->Release();
    }
};

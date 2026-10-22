// Runs on the Present thread, in the overlay's private ImGui context.
constexpr auto PanelFlags = ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize |
    ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoNav | ImGuiWindowFlags_NoSavedSettings;

void RegisterRegion(unsigned& count, float sx, float sy) {
    if (count >= 2) return;
    auto p = ImGui::GetWindowPos(), s = ImGui::GetWindowSize();
    regions[count].left = LONG(p.x/sx); regions[count].top = LONG(p.y/sy);
    regions[count].right = LONG((p.x+s.x)/sx); regions[count].bottom = LONG((p.y+s.y)/sy);
    ++count;
}

void ClampPanel(ImVec2 size) {
    auto display = ImGui::GetIO().DisplaySize, p = ImGui::GetWindowPos();
    ImGui::SetWindowPos(ImVec2(std::clamp(p.x, 0.f, (std::max)(0.f, display.x-size.x)),
        std::clamp(p.y, 0.f, (std::max)(0.f, display.y-size.y))));
}

void RecommendationRows(const char* text) {
    struct Row { std::string name,rate,samples; };
    static std::string previous;
    static std::vector<Row> rows;
    if(previous!=text) {
        previous=text; rows.clear();
        std::istringstream lines(previous); std::string line;
        while(std::getline(lines,line)) {
            auto percent=line.find('%'); if(percent==std::string::npos) continue;
            auto start=percent; while(start>0 && (isdigit(static_cast<unsigned char>(line[start-1])) || line[start-1]=='.')) --start;
            auto delimiter=line.find("·",percent);
            rows.push_back({line.substr(0,start),line.substr(start,percent-start+1),delimiter==std::string::npos ? "" : line.substr(delimiter+2)});
        }
    }
    if(rows.empty()) { ImGui::TextWrapped("%s",text); return; }
    if (ImGui::BeginTable("Rates", 3, ImGuiTableFlags_RowBg | ImGuiTableFlags_SizingStretchProp)) {
        ImGui::TableSetupColumn("增幅", ImGuiTableColumnFlags_WidthStretch, 1.8f);
        ImGui::TableSetupColumn("胜率", ImGuiTableColumnFlags_WidthFixed, 58);
        ImGui::TableSetupColumn("场次", ImGuiTableColumnFlags_WidthFixed, 84);
        ImGui::TableHeadersRow();
        ImGuiListClipper clipper; clipper.Begin(int(rows.size()),29);
        while(clipper.Step()) for(int i=clipper.DisplayStart;i<clipper.DisplayEnd;++i) {
            const auto& row=rows[i];
            ImGui::TableNextRow(0,29); ImGui::TableNextColumn(); ImGui::TextUnformatted(row.name.c_str());
            ImGui::TableNextColumn(); ImGui::TextColored(ImVec4(.55f,.85f,.70f,1),"%s",row.rate.c_str());
            ImGui::TableNextColumn(); ImGui::TextDisabled("%s",row.samples.c_str());
        }
        ImGui::EndTable();
    }
}

void DrawPanels(Session& current, Frame& frame, Auxiliary& aux, float sx, float sy) {
    auto display = ImGui::GetIO().DisplaySize;
    auto& style = ImGui::GetStyle();
    style.WindowPadding = ImVec2(14,14); style.ItemSpacing = ImVec2(8,9);
    style.WindowRounding = 12; style.ChildRounding = 8; style.FrameRounding = 6;
    style.Colors[ImGuiCol_WindowBg] = ImVec4(.055f,.075f,.115f,.97f);
    style.Colors[ImGuiCol_ChildBg] = ImVec4(.08f,.10f,.15f,1);
    style.Colors[ImGuiCol_Border] = ImVec4(.22f,.28f,.38f,.8f);
    style.Colors[ImGuiCol_Button] = ImVec4(.14f,.21f,.31f,1);
    style.Colors[ImGuiCol_ButtonHovered] = ImVec4(.22f,.35f,.49f,1);
    style.Colors[ImGuiCol_Header] = ImVec4(.20f,.31f,.46f,1);
    unsigned hits = 0;
    frame.title[255] = frame.statusText[2047] = frame.recommendations[32767] = frame.offered[16383] = 0;
    aux.coachTitle[255] = aux.coachBody[2047] = aux.coachDetail[1023] = aux.skinToast[1023] = 0;
    aux.skinTitle[255] = aux.skinStatus[2047] = 0;
    if (frame.mayhemEnabled) {
        const bool compact = frame.collapsed != 0;
        static bool positioned = false, wasCompact = false;
        static float verticalPosition = 0;
        ImVec2 size = compact ? ImVec2(48,48) : ImVec2((std::min)(400.f,display.x), (std::min)(566.f,display.y));
        ImGui::SetNextWindowPos(ImVec2((std::max)(0.f,display.x-size.x-18), (std::max)(0.f,(display.y-size.y)/2)),ImGuiCond_FirstUseEver);
        // Position before Begin so ImGui computes clipping and mouse hit boxes at
        // the docked location, rather than at the expanded panel's previous left edge.
        if (compact || wasCompact) {
            float y = positioned ? verticalPosition : (display.y-size.y)/2;
            ImGui::SetNextWindowPos(ImVec2((std::max)(0.f,display.x-size.x),
                std::clamp(y,0.f,(std::max)(0.f,display.y-size.y))));
        }
        ImGui::SetNextWindowSize(size);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, compact ? ImVec2(0,0) : ImVec2(14,14));
        ImGui::PushStyleVar(ImGuiStyleVar_WindowMinSize, ImVec2(48,48));
        ImGui::Begin("SC.MayhemOverlay",nullptr,PanelFlags | (compact ? ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoMove : 0));
        ClampPanel(size);
        positioned = true; wasCompact = compact; verticalPosition = ImGui::GetWindowPos().y;
        current.frame->panelWidth = uint32_t(size.x); current.frame->panelHeight = uint32_t(size.y);
        RegisterRegion(hits,sx,sy);
        if (compact) {
            static bool dragged = false;
            ImGui::PushStyleColor(ImGuiCol_Button,ImVec4(.24f,.20f,.38f,1));
            bool pressed = ImGui::Button("增幅",ImVec2(48,48));
            if (ImGui::IsItemActivated()) dragged = false;
            if (ImGui::IsItemActive() && ImGui::IsMouseDragging(0,6)) {
                dragged = true;
                verticalPosition = std::clamp(verticalPosition+ImGui::GetIO().MouseDelta.y,
                    0.f,(std::max)(0.f,display.y-size.y));
            }
            if (pressed && !dragged) current.pendingCommand=2;
            ImGui::PopStyleColor();
            if (ImGui::IsItemHovered()) ImGui::SetTooltip("点击展开增幅推荐 · 沿右侧上下拖动");
        } else {
            // One hit target covers the entire header, including its padding.
            // A drag moves the panel; only a click sends the collapse command.
            static bool headerDragged = false;
            float lineHeight = ImGui::GetTextLineHeight();
            ImGui::SetCursorPos(ImVec2(0,0));
            bool headerPressed = ImGui::InvisibleButton("CollapseHeader",
                ImVec2(size.x,style.WindowPadding.y+lineHeight*2+style.ItemSpacing.y));
            if (ImGui::IsItemActivated()) headerDragged = false;
            if (ImGui::IsItemActive() && ImGui::IsMouseDragging(0,6)) {
                headerDragged = true;
                auto p = ImGui::GetWindowPos(), delta = ImGui::GetIO().MouseDelta;
                ImGui::SetWindowPos(ImVec2(p.x+delta.x,p.y+delta.y));
                ClampPanel(size);
                verticalPosition = ImGui::GetWindowPos().y;
            }
            if (headerPressed && !headerDragged) current.pendingCommand=2;
            if (ImGui::IsItemHovered()) {
                ImGui::SetMouseCursor(ImGuiMouseCursor_Hand);
                ImGui::SetTooltip("点击收起增幅推荐 · 拖动移动面板");
            }
            auto p = ImGui::GetWindowPos();
            auto draw = ImGui::GetWindowDrawList();
            draw->AddText(ImVec2(p.x+14,p.y+14),ImGui::GetColorU32(ImVec4(.82f,.72f,1,1)),"海克斯大乱斗");
            draw->AddText(ImVec2(p.x+size.x-72,p.y+14),ImGui::GetColorU32(ImGuiCol_Text),"收起");
            draw->AddText(ImVec2(p.x+14,p.y+14+lineHeight+style.ItemSpacing.y),ImGui::GetColorU32(ImGuiCol_TextDisabled),"增幅推荐");
            ImGui::Separator();
            ImGui::PushTextWrapPos(); ImGui::TextDisabled("%s",frame.statusText); ImGui::PopTextWrapPos();
            ImGui::BeginDisabled(frame.scanning != 0);
            if (ImGui::Button(frame.scanning ? "正在识别…" : "扫描当前增幅",ImVec2(-1,35))) current.pendingCommand=1;
            ImGui::EndDisabled();
            const char* tabs[]={"白银","黄金","棱彩"};
            for(int i=0;i<3;++i) {
                if(i) ImGui::SameLine();
                ImGui::PushID(i);
                if(frame.rarity==unsigned(i)) ImGui::PushStyleColor(ImGuiCol_Button,ImVec4(.30f,.25f,.46f,1));
                if(ImGui::Button(tabs[i],ImVec2((ImGui::GetWindowWidth()-44)/3,30))) current.pendingCommand=10+i;
                if(frame.rarity==unsigned(i)) ImGui::PopStyleColor();
                ImGui::PopID();
            }
            float available = ImGui::GetContentRegionAvail().y;
            ImGui::BeginChild("Recommendations",ImVec2(0,(std::max)(70.f,available*.61f)),ImGuiChildFlags_Borders);
            RecommendationRows(frame.recommendations); ImGui::EndChild();
            ImGui::BeginChild("Offers",ImVec2(0,0),ImGuiChildFlags_Borders);
            ImGui::TextColored(ImVec4(.73f,.81f,.94f,1),"本次识别");
            const char* offered=frame.offered;
            if(strncmp(offered,"本次识别\n",13)==0) offered+=13;
            ImGui::TextWrapped("%s",*offered ? offered : "扫描后显示当前增幅与推荐"); ImGui::EndChild();
        }
        ImGui::End(); ImGui::PopStyleVar(2);
    } else { current.frame->panelWidth=0; current.frame->panelHeight=0; }
    if ((aux.flags & 1) && aux.battleWidth > 0 && aux.battleHeight > 0 && aux.battleWidth <= 1600 && aux.battleHeight <= 1000) {
        ComPtr<ID3D11Device> textureDevice;
        if(current.battleTexture) current.battleTexture->GetDevice(&textureDevice);
        if(current.textureVersion != aux.battleVersion || textureDevice.Get() != device.Get()) {
            DWORD acquired=WaitForSingleObject(current.mutex,0);
            if(acquired==WAIT_OBJECT_0 || acquired==WAIT_ABANDONED) {
                auto source=reinterpret_cast<Auxiliary*>(reinterpret_cast<uint8_t*>(current.frame)+AuxOffset);
                if(source->battleVersion==aux.battleVersion) {
                    D3D11_TEXTURE2D_DESC d{}; d.Width=aux.battleWidth; d.Height=aux.battleHeight;
                    d.MipLevels=d.ArraySize=1; d.Format=DXGI_FORMAT_B8G8R8A8_UNORM; d.SampleDesc.Count=1;
                    d.Usage=D3D11_USAGE_IMMUTABLE; d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
                    D3D11_SUBRESOURCE_DATA pixels{reinterpret_cast<uint8_t*>(current.frame)+BattlePixelOffset,aux.battleWidth*4,0};
                    ComPtr<ID3D11Texture2D> texture;
                    if(SUCCEEDED(device->CreateTexture2D(&d,&pixels,&texture))) {
                        current.battleTexture.Reset();
                        if(SUCCEEDED(device->CreateShaderResourceView(texture.Get(),nullptr,&current.battleTexture))) current.textureVersion=aux.battleVersion;
                    }
                }
                ReleaseMutex(current.mutex);
            }
        }
        if(current.battleTexture) {
            float factor=(std::min)(1.f,(std::min)(display.x*.92f/aux.battleWidth,display.y*.85f/aux.battleHeight));
            ImVec2 size(aux.battleWidth*factor,aux.battleHeight*factor);
            ImGui::GetBackgroundDrawList()->AddImage((ImTextureID)current.battleTexture.Get(),ImVec2((display.x-size.x)/2,(display.y-size.y)/2),ImVec2((display.x+size.x)/2,(display.y+size.y)/2));
        }
    }
    if((aux.flags & 2) && (!aux.coachDeadline || GetTickCount64()<aux.coachDeadline)) {
        ImVec2 size((std::min)(450.f,display.x),0), anchor(0,1);
        ImVec2 pos(float(aux.coachOffsetX),display.y-aux.coachOffsetY);
        if(aux.coachPosition==1) { pos.y=float(aux.coachOffsetY); anchor.y=0; }
        if(aux.coachPosition==2 || aux.coachPosition==3) { pos.x=display.x-aux.coachOffsetX; anchor.x=1; }
        if(aux.coachPosition==2) { pos.y=float(aux.coachOffsetY); anchor.y=0; }
        if(aux.coachPosition==4) { pos=ImVec2(display.x/2+aux.coachOffsetX,display.y/2+aux.coachOffsetY); anchor=ImVec2(.5f,.5f); }
        ImGui::SetNextWindowPos(pos,ImGuiCond_Always,anchor); ImGui::SetNextWindowSize(size);
        ImGui::Begin("SC.Coaching",nullptr,PanelFlags|ImGuiWindowFlags_AlwaysAutoResize|ImGuiWindowFlags_NoInputs);
        ImGui::TextColored(ImVec4(.51f,.77f,1,1),"当前时间线建议");
        ImGui::TextWrapped("%s",aux.coachTitle); ImGui::Separator(); ImGui::TextWrapped("%s",aux.coachBody);
        ImGui::PushTextWrapPos(); ImGui::TextDisabled("%s",aux.coachDetail); ImGui::PopTextWrapPos(); ImGui::End();
    }
    if((aux.flags & 4) && (!aux.skinDeadline || GetTickCount64()<aux.skinDeadline)) {
        ImGui::SetNextWindowPos(ImVec2(display.x/2,display.y*.12f),ImGuiCond_Always,ImVec2(.5f,0));
        ImGui::SetNextWindowSize(ImVec2((std::min)(460.f,display.x),0));
        ImGui::Begin("SC.SkinNotice",nullptr,PanelFlags|ImGuiWindowFlags_AlwaysAutoResize|ImGuiWindowFlags_NoInputs);
        ImGui::TextColored(ImVec4(.55f,.87f,.72f,1),"皮肤已切换"); ImGui::TextWrapped("%s",aux.skinToast); ImGui::End();
    }
    if(aux.flags & 8) {
        ImVec2 size((std::min)(360.f,display.x),(std::min)(520.f,display.y));
        ImGui::SetNextWindowPos(ImVec2(24,(std::max)(0.f,(display.y-size.y)/2)),ImGuiCond_FirstUseEver); ImGui::SetNextWindowSize(size);
        ImGui::Begin("SC.SkinPicker",nullptr,PanelFlags); ClampPanel(size); RegisterRegion(hits,sx,sy);
        auto command=[&](LONG action,LONG argument=0) { current.pendingAux=action;current.pendingArgument=argument;current.pendingVersion=aux.skinVersion; };
        ImGui::TextColored(ImVec4(.82f,.72f,1,1),"游戏换肤"); ImGui::SameLine(ImGui::GetWindowWidth()-72);
        if(ImGui::SmallButton("隐藏")) command(4);
        ImGui::PushTextWrapPos(); ImGui::TextDisabled("%s",aux.skinTitle); ImGui::PopTextWrapPos(); ImGui::Separator();
        bool cameraEnabled=aux.cameraEnabled!=0;
        ImGui::BeginDisabled(!aux.cameraReady);
        if(ImGui::Checkbox("无限视距",&cameraEnabled)) command(7,cameraEnabled ? 1 : 0);
#ifdef SC_OVERLAY_TEST
        { auto p0=ImGui::GetItemRectMin(),p1=ImGui::GetItemRectMax(); testCameraToggle={LONG(p0.x/sx),LONG(p0.y/sy),LONG(p1.x/sx),LONG(p1.y/sy)}; }
#endif
        ImGui::EndDisabled();
        ImGui::TextDisabled("滚轮平缓调节，中键恢复游戏原本视距");
        ImGui::BeginDisabled(!aux.skinCanRefresh);
        const char* categories[]={"英雄皮肤","眼位皮肤","小兵皮肤"};
        for(int category=0;category<3;++category) {
            if(category) ImGui::SameLine();
            bool active=aux.skinCategory==category;
            if(active) ImGui::PushStyleColor(ImGuiCol_Button,ImVec4(.15f,.38f,.60f,1));
            if(ImGui::Button(categories[category],ImVec2(100,30))) command(5,category);
#ifdef SC_OVERLAY_TEST
            auto p0=ImGui::GetItemRectMin(),p1=ImGui::GetItemRectMax();
            testSkinCategories[category]={LONG(p0.x/sx),LONG(p0.y/sy),LONG(p1.x/sx),LONG(p1.y/sy)};
#endif
            if(active) ImGui::PopStyleColor();
        }
        ImGui::EndDisabled();
        ImGui::BeginChild("Skins",ImVec2(0,(std::max)(40.f,ImGui::GetContentRegionAvail().y-145)),ImGuiChildFlags_Borders);
        static int previousSkinVersion=-1;
        if(previousSkinVersion!=aux.skinVersion) { ImGui::SetScrollY(0); previousSkinVersion=aux.skinVersion; }
        ImGui::BeginDisabled(!aux.skinReady);
        ImGui::PushID(aux.skinVersion); // A held mouse click cannot complete on a replacement list.
        ImGuiListClipper skins; skins.Begin(std::clamp(aux.skinCount,0,256));
        while(skins.Step()) for(int i=skins.DisplayStart;i<skins.DisplayEnd;++i) {
            aux.skinNames[i][191]=0; ImGui::PushID(i);
            auto textPos=ImGui::GetCursorScreenPos();
            if(ImGui::Selectable("##skin",aux.skinSelected==i,0,ImVec2(0,32))) command(1,i);
            const char* name=aux.skinNames[i];size_t length=strlen(name);
            uint32_t prefix=aux.skinPrefixBytes[i];
            textPos.y+=(32-ImGui::GetTextLineHeight())*.5f;
            auto draw=ImGui::GetWindowDrawList();
            if(prefix>0 && prefix<=length && prefix<192 && (prefix==length || (static_cast<unsigned char>(name[prefix])&0xc0)!=0x80)) {
                ImU32 color=aux.skinPrefixColors[i];
                if(!aux.skinReady) color=(color&0x00ffffff)|0x80000000;
                draw->AddText(textPos,color,name,name+prefix);
                textPos.x+=ImGui::CalcTextSize(name,name+prefix).x;
                draw->AddText(textPos,ImGui::GetColorU32(ImGuiCol_Text),name+prefix);
            } else draw->AddText(textPos,ImGui::GetColorU32(ImGuiCol_Text),name);
#ifdef SC_OVERLAY_TEST
            if(i==0) { auto p0=ImGui::GetItemRectMin(),p1=ImGui::GetItemRectMax(); testSkinFirstEntry={LONG(p0.x/sx),LONG(p0.y/sy),LONG(p1.x/sx),LONG(p1.y/sy)}; }
#endif
            ImGui::PopID();
        }
        ImGui::PopID(); ImGui::EndDisabled();
#ifdef SC_OVERLAY_TEST
        testSkinScroll=ImGui::GetScrollY();
#endif
        ImGui::EndChild();
        ImGui::BeginDisabled(!aux.skinCanRefresh); if(ImGui::Button("重新读取")) command(2); ImGui::EndDisabled();
        ImGui::SameLine(); ImGui::BeginDisabled(!aux.skinReady); if(ImGui::Button(aux.skinCategory==0 ? "恢复基础皮肤" : "恢复小兵与眼位")) command(3); ImGui::EndDisabled();
        if(aux.skinPageCount>1) {
            ImGui::BeginDisabled(!aux.skinCanRefresh || aux.skinPage<=0); if(ImGui::SmallButton("上一页")) command(6,-1); ImGui::EndDisabled();
#ifdef SC_OVERLAY_TEST
            { auto p0=ImGui::GetItemRectMin(),p1=ImGui::GetItemRectMax(); testSkinPages[0]={LONG(p0.x/sx),LONG(p0.y/sy),LONG(p1.x/sx),LONG(p1.y/sy)}; }
#endif
            ImGui::SameLine(); ImGui::Text("%d / %d",aux.skinPage+1,aux.skinPageCount); ImGui::SameLine();
            ImGui::BeginDisabled(!aux.skinCanRefresh || aux.skinPage+1>=aux.skinPageCount); if(ImGui::SmallButton("下一页")) command(6,1); ImGui::EndDisabled();
#ifdef SC_OVERLAY_TEST
            { auto p0=ImGui::GetItemRectMin(),p1=ImGui::GetItemRectMax(); testSkinPages[1]={LONG(p0.x/sx),LONG(p0.y/sy),LONG(p1.x/sx),LONG(p1.y/sy)}; }
#endif
        }
        ImGui::BeginChild("Status",ImVec2(0,0)); ImGui::PushTextWrapPos(); ImGui::TextDisabled("%s",aux.skinStatus); ImGui::PopTextWrapPos(); ImGui::EndChild();
        ImGui::End();
    }
    regionCount=hits; acceptsMouse=hits>0; lastRender=GetTickCount64(); current.frame->hitRegionCount=hits;
}

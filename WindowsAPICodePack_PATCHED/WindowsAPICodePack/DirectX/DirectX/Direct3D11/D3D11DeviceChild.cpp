// Copyright (c) Microsoft Corporation.  All rights reserved.

#include "stdafx.h"
#include "D3D11DeviceChild.h"

#include "D3D11Device.h"
using namespace Microsoft::WindowsAPICodePack::DirectX::Direct3D11;

D3DDevice^ DeviceChild::GetDevice()
{
    ID3D11Device* tempoutDevice = NULL;
    GetInterface<ID3D11DeviceChild>()->GetDevice(&tempoutDevice);
    return tempoutDevice == NULL ? nullptr : gcnew D3DDevice(tempoutDevice);
}

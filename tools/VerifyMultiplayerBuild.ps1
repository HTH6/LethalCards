param(
    [string]$AssemblyPath = "$PSScriptRoot/../bin/Release/netstandard2.1/LethalCards.dll",
    [string]$GameManagedPath = 'Z:/SteamLibrary/steamapps/common/Lethal Company/Lethal Company_Data/Managed',
    [string]$CecilPath = "$env:USERPROFILE/.nuget/packages/mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll"
)

$ErrorActionPreference = 'Stop'
Add-Type -Path $CecilPath
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($GameManagedPath)
$readerParameters = New-Object Mono.Cecil.ReaderParameters
$readerParameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $AssemblyPath).Path, $readerParameters)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$GameManagedPath/Assembly-CSharp.dll", $readerParameters)

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

try {
    # An ordinary C# build succeeds even when RPCs are never rewritten.
    foreach ($spec in @(
        @('LethalCards.Boosters.BoosterPackBehaviour', 'RequestOpenPackServerRpc', '__beginSendServerRpc'),
        @('LethalCards.Boosters.BoosterPackBehaviour', 'NotifyRevealClientRpc', '__beginSendClientRpc'),
        @('LethalCards.Boosters.BoosterBoxBehaviour', 'RequestOpenBoxServerRpc', '__beginSendServerRpc'),
        @('LethalCards.Binders.CardBinderBehaviour', 'StoreCardServerRpc', '__beginSendServerRpc'),
        @('LethalCards.Binders.CardBinderBehaviour', 'ApplyBinderValueClientRpc', '__beginSendClientRpc'),
        @('LethalCards.Networking.NetworkItemConsumption', 'ClearInventoryClientRpc', '__beginSendClientRpc'),
        @('LethalCards.ShipDisplay.ShipCardDisplayController', 'InteractServerRpc', '__beginSendServerRpc'),
        @('LethalCards.ShipDisplay.ShipCardDisplayController', 'ConfirmAttachmentServerRpc', '__beginSendServerRpc'),
        @('LethalCards.ShipDisplay.ShipCardDisplayController', 'AttachCardClientRpc', '__beginSendClientRpc'),
        @('LethalCards.ShipDisplay.ShipCardDisplayController', 'InteractionResultClientRpc', '__beginSendClientRpc')
    )) {
        $type = $assembly.MainModule.GetType($spec[0])
        $rpc = $type.Methods | Where-Object Name -eq $spec[1]
        Assert-True ($null -ne $rpc) "Missing RPC: $($spec[1])"
        Assert-True (@($rpc.Body.Instructions | Where-Object { $_.Operand.Name -eq $spec[2] }).Count -gt 0) "Unpatched RPC: $($spec[1])"
        Assert-True (@($type.Methods | Where-Object Name -like '__rpc_handler_*').Count -gt 0) "Missing receive handler: $($spec[0])"
    }

    $displayController = $assembly.MainModule.GetType('LethalCards.ShipDisplay.ShipCardDisplayController')
    $displayInitializer = $displayController.Methods | Where-Object Name -eq '__initializeVariables'
    $displaySlots = $displayController.Fields | Where-Object Name -eq 'networkSlots'
    Assert-True ($null -ne $displayInitializer) 'Missing display controller variable initializer'
    Assert-True ($null -ne $displaySlots -and $displaySlots.FieldType.FullName.StartsWith('Unity.Netcode.NetworkList`1')) 'Missing display slot NetworkList'
    Assert-True (@($displayInitializer.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'networkSlots' }).Count -gt 0) 'Unregistered display slot NetworkList'
    $displaySpawn = $displayController.Methods | Where-Object Name -eq 'TrySpawnIntoInventory'
    Assert-True (@($displaySpawn.Body.Instructions | Where-Object { $_.Operand.Name -eq 'SpawnWithOwnership' }).Count -eq 1) 'Display reconstruction does not spawn with its final owner'
    Assert-True (@($displaySpawn.Body.Instructions | Where-Object { $_.Operand.Name -eq 'ChangeOwnership' }).Count -eq 0) 'Display reconstruction still performs a separate ownership transfer'
    $snapshotValidation = $displayController.Methods | Where-Object Name -eq 'TryValidateInteractionSnapshot'
    Assert-True ($null -ne $snapshotValidation) 'Missing request-time display interaction snapshot validation'
    Assert-True (@($snapshotValidation.Body.Instructions | Where-Object { $_.Operand.Name -eq 'get_gameplayCamera' }).Count -eq 0) 'Server snapshot validation still reads the remote gameplay camera'
    $currentAccessValidation = $displayController.Methods | Where-Object Name -eq 'ValidateCurrentPlayerAccess'
    Assert-True ($null -ne $currentAccessValidation) 'Missing post-settlement distance and occlusion validation'
    Assert-True (@($currentAccessValidation.Body.Instructions | Where-Object { $_.Operand.Name -eq 'get_gameplayCamera' }).Count -eq 0) 'Post-settlement validation still reads the remote gameplay camera'
    $occlusionValidation = $displayController.Methods | Where-Object Name -eq 'IsCabinetFirstUnobstructedHit'
    Assert-True (@($occlusionValidation.Body.Instructions | Where-Object { $_.Operand.Name -eq 'IsColliderOwnedByPlayerInventory' }).Count -gt 0) 'Display occlusion validation does not exclude the requesting player inventory'
    $terminalResult = $displayController.Methods | Where-Object Name -eq 'SendTerminalResult'
    Assert-True ($null -ne $terminalResult) 'Missing exactly-once display transaction terminal result handling'
    $applyDisplayCard = $displayController.Methods | Where-Object Name -eq 'ApplyCardToInventory'
    Assert-True (@($applyDisplayCard.Body.Instructions | Where-Object { $_.Operand.Name -eq 'RefreshPhysicalScanNodeVisibility' }).Count -gt 0) 'Reconstructed cards do not immediately refresh physical scan-node visibility'
    Assert-True (@($applyDisplayCard.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'propColliders' }).Count -eq 1) 'Reconstructed cards do not initialize vanilla propColliders before disabling physics'
    $directResultSenders = @($displayController.Methods | Where-Object {
        $_.HasBody -and @($_.Body.Instructions | Where-Object { $_.Operand.Name -eq 'SendResult' }).Count -gt 0
    })
    Assert-True ($directResultSenders.Count -eq 1 -and $directResultSenders[0].Name -eq 'SendTerminalResult') 'A display transaction can bypass exactly-once terminal result handling'
    $displayInteraction = $assembly.MainModule.GetType('LethalCards.ShipDisplay.ShipCardDisplayInteractionPatch')
    $localTargeting = $displayInteraction.Methods | Where-Object Name -eq 'TryGetTarget'
    Assert-True (@($localTargeting.Body.Instructions | Where-Object { $_.Operand.Name -eq 'IsColliderOwnedByPlayerInventory' }).Count -gt 0) 'Local display targeting does not exclude the requesting player inventory'
    $attachmentAck = $displayController.Methods | Where-Object Name -eq 'ConfirmAttachmentServerRpc'
    Assert-True ($null -ne $attachmentAck) 'Missing reconstructed-card owner acknowledgment'
    $attachmentStateMachine = $displayController.NestedTypes | Where-Object Name -like '*AttachCardWhenReady*'
    $attachmentMoveNext = $attachmentStateMachine.Methods | Where-Object Name -eq 'MoveNext'
    Assert-True (@($attachmentMoveNext.Body.Instructions | Where-Object { $_.Operand.Name -eq 'ValidateInventoryPostconditions' }).Count -gt 0) 'Client attachment does not validate its final inventory state'
    Assert-True (@($attachmentMoveNext.Body.Instructions | Where-Object { $_.Operand.Name -eq 'ConfirmAttachmentServerRpc' }).Count -gt 0) 'Client attachment does not acknowledge validated completion'
    Assert-True (@($displayController.Methods | Where-Object Name -eq 'ValidateRequestedSlotAim').Count -eq 0) 'Obsolete delayed camera-ray validation is still present'

    foreach ($typeName in @('LethalCards.Cards.CardInstanceData', 'LethalCards.Grading.GradingReturnData')) {
        $type = $assembly.MainModule.GetType($typeName)
        $initializer = $type.Methods | Where-Object Name -eq '__initializeVariables'
        Assert-True ($null -ne $initializer) "Missing variable initializer: $typeName"
        $variables = @($type.Fields | Where-Object { $_.FieldType.FullName.StartsWith('Unity.Netcode.NetworkVariable`1') })
        Assert-True ($variables.Count -gt 0) "No network variables found: $typeName"
        foreach ($field in $variables) {
            Assert-True (@($initializer.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq $field.Name }).Count -gt 0) "Unregistered variable: $typeName.$($field.Name)"
        }
    }
    $cardData = $assembly.MainModule.GetType('LethalCards.Cards.CardInstanceData')
    $refreshScanMetadata = $cardData.Methods | Where-Object Name -eq 'RefreshScanNodes'
    $refreshScanVisibility = $cardData.Methods | Where-Object Name -eq 'RefreshPhysicalScanNodeVisibility'
    Assert-True ($null -ne $refreshScanVisibility) 'Missing physical card scan-node state reconciliation'
    Assert-True (@($refreshScanMetadata.Body.Instructions | Where-Object { $_.Operand.Name -eq 'SetActive' }).Count -eq 0) 'Card metadata refresh still directly reactivates scan-node objects'

    $serialization = $assembly.MainModule.GetType('__GEN.NetworkVariableSerializationHelper')
    Assert-True ($null -ne $serialization) 'Missing generated variable serialization initialization'
    Assert-True (@($serialization.Methods | Where-Object { $_.CustomAttributes.AttributeType.Name -contains 'RuntimeInitializeOnLoadMethodAttribute' }).Count -gt 0) 'Missing Unity serialization startup attribute'

    # Ensure patcher-generated calls exist in the installed game's Netcode DLL.
    $checkedReferences = 0
    foreach ($member in $assembly.MainModule.GetMemberReferences()) {
        if ($member.DeclaringType.Scope.Name -ne 'Unity.Netcode.Runtime') { continue }
        Assert-True ($null -ne $member.Resolve()) "Netcode member not present in game: $member"
        $checkedReferences++
    }

    $player = $game.MainModule.GetType('GameNetcodeStuff.PlayerControllerB')
    foreach ($methodName in @('BeginGrabObject', 'SetHoverTipAndCurrentInteractTrigger')) {
        Assert-True (@($player.Methods | Where-Object Name -eq $methodName).Count -eq 1) "Binder interaction hook missing or ambiguous: $methodName"
    }
    Assert-True (@($player.Fields | Where-Object Name -eq 'interactableObjectsMask').Count -eq 1) 'Binder interaction mask field is missing or ambiguous'
    $pickup = $player.Methods | Where-Object Name -eq 'GrabObjectServerRpc'
    Assert-True ($pickup.Parameters.Count -eq 1 -and $pickup.Parameters[0].Name -eq 'grabbedObject') 'Pickup Harmony hook no longer matches the game'
    Assert-True (@($pickup.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'heldByPlayerOnServer' }).Count -gt 0) 'Game pickup no longer marks server-held state'
    Assert-True (@($pickup.Body.Instructions | Where-Object { $_.Operand.Name -eq 'ChangeOwnership' }).Count -gt 0) 'Game pickup no longer transfers ownership'

    $round = $game.MainModule.GetType('StartOfRound')
    foreach ($methodName in @('Start', 'Update', 'OnDestroy', 'ChangeLevel', 'StartGame')) {
        Assert-True (@($round.Methods | Where-Object Name -eq $methodName).Count -eq 1) "Lifecycle hook missing or ambiguous: $methodName"
    }
    $roundManager = $game.MainModule.GetType('RoundManager')
    Assert-True (@($roundManager.Methods | Where-Object Name -eq 'DespawnPropsAtEndOfRound').Count -eq 1) 'Crew-wipe persistence hook is missing or ambiguous'
    Assert-True (@($player.Methods | Where-Object Name -eq 'SwitchToItemSlot').Count -eq 1) 'Exact-slot inventory handoff method is missing or ambiguous'

    # Museum uses authenticated Netcode custom messages rather than a networked
    # scene model. Verify its persistence, synchronization, interaction, and
    # authored-prefab integration entry points survive release trimming/builds.
    foreach ($typeName in @(
        'LethalCards.Museum.CompanyMuseumManager',
        'LethalCards.Museum.CompanyMuseumSaveManager',
        'LethalCards.Museum.CompanyMuseumNetworkSync',
        'LethalCards.Museum.CompanyMuseumDisplayController',
        'LethalCards.Museum.CompanyMuseumInteractionPatch',
        'LethalCards.Museum.CompanyMuseumDisplaySpawner',
        'LethalCards.Museum.CompanyMuseumShipDisplayIntegration'
    )) {
        Assert-True ($null -ne $assembly.MainModule.GetType($typeName)) "Missing museum type: $typeName"
    }
    $museumNetwork = $assembly.MainModule.GetType('LethalCards.Museum.CompanyMuseumNetworkSync')
    foreach ($methodName in @('Initialize', 'EnsureSnapshot', 'BroadcastAuthoritativeSnapshot', 'RequestRegistration', 'ProcessAuthoritativeRegistration', 'Shutdown')) {
        Assert-True (@($museumNetwork.Methods | Where-Object Name -eq $methodName).Count -eq 1) "Missing museum network method: $methodName"
    }
    $museumSave = $assembly.MainModule.GetType('LethalCards.Museum.CompanyMuseumSaveManager')
    foreach ($methodName in @('LoadForCurrentSave', 'TrySave', 'ResetLoadedSave')) {
        Assert-True (@($museumSave.Methods | Where-Object Name -eq $methodName).Count -eq 1) "Missing museum save method: $methodName"
    }
    $museumSpawner = $assembly.MainModule.GetType('LethalCards.Museum.CompanyMuseumDisplaySpawner')
    foreach ($methodName in @('LoadAssets', 'TrySpawn', 'Reset')) {
        Assert-True (@($museumSpawner.Methods | Where-Object Name -eq $methodName).Count -eq 1) "Missing museum spawner method: $methodName"
    }
    $museumShipIntegration = $assembly.MainModule.GetType('LethalCards.Museum.CompanyMuseumShipDisplayIntegration')
    foreach ($methodName in @('Initialize', 'Update', 'Shutdown', 'OnAuthoritativeSlotChanged')) {
        Assert-True (@($museumShipIntegration.Methods | Where-Object Name -eq $methodName).Count -eq 1) "Missing museum ship integration method: $methodName"
    }
    $museumShipInitialize = $museumShipIntegration.Methods | Where-Object Name -eq 'Initialize'
    $museumShipShutdown = $museumShipIntegration.Methods | Where-Object Name -eq 'Shutdown'
    $museumShipCommit = $museumShipIntegration.Methods | Where-Object Name -eq 'TryCommit'
    Assert-True (@($museumShipInitialize.Body.Instructions | Where-Object { $_.Operand.Name -eq 'add_AuthoritativeSlotChanged' }).Count -eq 1) 'Museum does not subscribe to the authoritative ship transaction event'
    Assert-True (@($museumShipShutdown.Body.Instructions | Where-Object { $_.Operand.Name -eq 'remove_AuthoritativeSlotChanged' }).Count -eq 1) 'Museum does not unsubscribe from the authoritative ship transaction event'
    Assert-True (@($museumShipCommit.Body.Instructions | Where-Object { $_.Operand.Name -eq 'TryRegisterHighest' }).Count -eq 1) 'Museum ship integration does not use highest-grade registration'

    $plugin = $assembly.MainModule.GetType('LethalCards.Plugin')
    Assert-True (@($plugin.Properties | Where-Object Name -eq 'EnableDebugLogging').Count -eq 1) 'Missing centralized debug logging setting'
    Assert-True ($null -ne $assembly.MainModule.GetType('LethalCards.ModLog')) 'Missing centralized logging facade'
    $silentAudio = $assembly.MainModule.GetType('LethalCards.Audio.SilentItemAudio')
    Assert-True ($null -ne $silentAudio) 'Missing silent item audio compatibility helper'
    Assert-True (@($silentAudio.Methods | Where-Object Name -eq 'EnsureFallbacks').Count -eq 1) 'Missing silent item audio assignment method'
    $playerDebug = $assembly.MainModule.GetType('LethalCards.Debugging.PlayerPositionDebugPatch')
    $playerDebugPostfix = $playerDebug.Methods | Where-Object Name -eq 'Postfix'
    Assert-True (@($playerDebugPostfix.Body.Instructions | Where-Object { $_.Operand.Name -eq 'get_EnableDebugLogging' }).Count -gt 0) 'Player position diagnostics are not gated by EnableDebugLogging'
    Write-Output "PASS: RPC rewriting, card/return variables, serialization startup, $checkedReferences Netcode references, and game hook contracts."
    Write-Output 'PASS: Museum prefab, persistence, interaction, and custom-message synchronization contracts.'
    Write-Output 'This verifies the build and installed APIs; run the host/client checklist for gameplay validation.'
}
finally {
    $assembly.Dispose()
    $game.Dispose()
    $resolver.Dispose()
}

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
        @('LethalCards.Networking.NetworkItemConsumption', 'ClearInventoryClientRpc', '__beginSendClientRpc')
    )) {
        $type = $assembly.MainModule.GetType($spec[0])
        $rpc = $type.Methods | Where-Object Name -eq $spec[1]
        Assert-True ($null -ne $rpc) "Missing RPC: $($spec[1])"
        Assert-True (@($rpc.Body.Instructions | Where-Object { $_.Operand.Name -eq $spec[2] }).Count -gt 0) "Unpatched RPC: $($spec[1])"
        Assert-True (@($type.Methods | Where-Object Name -like '__rpc_handler_*').Count -gt 0) "Missing receive handler: $($spec[0])"
    }

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
    $pickup = $player.Methods | Where-Object Name -eq 'GrabObjectServerRpc'
    Assert-True ($pickup.Parameters.Count -eq 1 -and $pickup.Parameters[0].Name -eq 'grabbedObject') 'Pickup Harmony hook no longer matches the game'
    Assert-True (@($pickup.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq 'heldByPlayerOnServer' }).Count -gt 0) 'Game pickup no longer marks server-held state'
    Assert-True (@($pickup.Body.Instructions | Where-Object { $_.Operand.Name -eq 'ChangeOwnership' }).Count -gt 0) 'Game pickup no longer transfers ownership'

    $round = $game.MainModule.GetType('StartOfRound')
    foreach ($methodName in @('Start', 'Update', 'OnDestroy', 'ChangeLevel')) {
        Assert-True (@($round.Methods | Where-Object Name -eq $methodName).Count -eq 1) "Lifecycle hook missing or ambiguous: $methodName"
    }
    Write-Output "PASS: RPC rewriting, card/return variables, serialization startup, $checkedReferences Netcode references, and game hook contracts."
    Write-Output 'This verifies the build and installed APIs; run the host/client checklist for gameplay validation.'
}
finally {
    $assembly.Dispose()
    $game.Dispose()
    $resolver.Dispose()
}

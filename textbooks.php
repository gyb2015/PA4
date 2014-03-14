<html>
<head>
<title>Online Php Script Execution</title>
</head>
<body>
<p>helol</p>
<?php
print 'hello';
try 
{
  $conn = new PDO('mysql:host= awsuser.c3lb8gup7igo.us-west-2.rds.amazonaws.com;dbname=mydb', 'info344user', '340344380=9.0');
 
  $stmt = $conn->prepare("SELECT * FROM nbaplayers WHERE playerName LIKE '%Josh Smith%'");
  $stmt->execute(); 
  $result = $stmt->fetchAll();
  if ( count($result))
    {
     foreach($result as $row)
     {
       print $row['playerName']. ' at '.$row['GP'].' at '.$row['FGP']. 'at '.$row['TPP']. 'at '.$row['FTP']. 'at '.$row['PPG'].'<br>';
      }
    }
    else
    {
       echo "No rows returned.";       
    }
}
catch(PDOException $e)
{
  echo 'Error: ' . $e->getMessage();
}
    
    
?>
</body>
</html>
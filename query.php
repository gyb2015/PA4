<?php header('content-type: application/json; charset=utf-8');
try 
{
  $conn = new PDO('mysql:host=mydb0305.c3lb8gup7igo.us-west-2.rds.amazonaws.com; dbname=mydb', 'info344user', '123456QWas');
  $stmt = $conn->prepare("SELECT * FROM nbaplayers WHERE playerName LIKE '%".$_GET["playerName"]."%'");
  $stmt->execute(); 
  $result = $stmt->fetchAll();

  if ( count($result))
    {
     echo $_GET['callback'] . '('.json_encode($result).')';
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